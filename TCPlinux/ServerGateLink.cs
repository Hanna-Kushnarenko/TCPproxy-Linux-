using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Config;
namespace TCPlinux;

    public class ServerGateLink : CryptoLink, IDisposable
    {
        // Fields
        private ServerControlLink controlLink_;
        private Socket clientControlSocket_;
        private string clientControlAddress_;
        private TcpListener listener_;
        private int keepAliveTimeout_ = 60000;
        private DateTime lastAliveChecked_ = DateTime.MinValue;
        private bool isDisposed_;
        private List<SourcePoint> clientsAllowed_;
        private RijndaelManaged rijndael_;
        private static Mutex clientGateMutex_ = new Mutex();
        private static Socket clientSocket_ = (Socket)null;
        private static DateTime connectPendingUntil_ = DateTime.MinValue;

        public ServerGateLink(
     ServerControlLink controlLink,
     Socket clientSocket,
     int keepAliveTimeout,
     List<SourcePoint> clientsAllowed)
        {
            this.controlLink_ = controlLink;
            this.clientControlSocket_ = clientSocket;
            this.clientControlAddress_ = this.clientControlSocket_.RemoteEndPoint.ToString();
            this.keepAliveTimeout_ = keepAliveTimeout;
            this.clientsAllowed_ = clientsAllowed;
            if (controlLink.Key != null)
            {
                this.rijndael_ = new RijndaelManaged();
                this.rijndael_.Padding = PaddingMode.None;
                this.rijndael_.GenerateIV();
                this.rijndael_.Key = controlLink.Key;
                this.Decryptor = this.rijndael_.CreateDecryptor();
                this.Encryptor = this.rijndael_.CreateEncryptor();
            }
            this.StartAuthentication();
        }

        public void ProcessPending()
        {
            if (this.CheckPendingGateConnect())
                return;
            if (this.listener_ != null)
            {
                try
                {
                    if (this.listener_.Pending())
                    {
                        Socket cs = this.listener_.AcceptSocket();
                        IPEndPoint remoteEndPoint = cs.RemoteEndPoint as IPEndPoint;
                        if (SourcePoint.IsInside(((IPEndPoint)cs.RemoteEndPoint).Address, this.clientsAllowed_))
                        {
                            this.StartGateConnection(cs);
                        }
                        else
                        {
                            Logger.Warning("Unauthorized connection to '{0}' from '{1}' closed", (object)this.clientControlAddress_, (object)remoteEndPoint);
                            cs.Shutdown(SocketShutdown.Both);
                            cs.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Can't accept socket on '{0}'", ex, (object)this.listener_.LocalEndpoint);
                }
            }
            if (this.CheckPendingGateConnect())
                return;
            this.CheckConnected();
        }

        private void CheckConnected()
        {
            if (!(this.lastAliveChecked_.AddMilliseconds((double)(this.keepAliveTimeout_ / 2)) < DateTime.UtcNow))
                return;
            this.lastAliveChecked_ = DateTime.UtcNow;
            try
            {
                byte[] buffer = this.Encrypt("ping");
                TcpProxyManager.Instance.EnterAsyncOperation();
                this.clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(this.PingSent), (object)this);
            }
            catch (Exception ex)
            {
                this.HandleError("Connection from '{0}' closed", ex, (object)this.clientControlAddress_);
            }
        }

        private void StartAuthentication()
        {
            TcpProxyManager.Instance.EnterAsyncOperation();
            byte[] buffer = new byte[16];
            if (this.rijndael_ != null)
                buffer = this.rijndael_.IV;
            this.clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(this.AuthenticationSent), (object)this);
        }

        private void AuthenticationSent(IAsyncResult ar)
        {
            if (this.isDisposed_)
                return;
            try
            {
                try
                {
                    this.clientControlSocket_.EndSend(ar);
                }
                catch (Exception ex)
                {
                    this.HandleError("Can't send IV to '{0}'", ex, (object)this.clientControlAddress_);
                    return;
                }
                string handle = string.Empty;
                Config.EndPoint endPoint = new Config.EndPoint();
                bool flag;
                try
                {
                    string[] strArray = this.ReceiveAndDecrypt(this.clientControlSocket_).Split(' ');
                    if (strArray.Length == 3 && strArray[0] == "listen")
                    {
                        flag = true;
                        endPoint.IP = strArray[1];
                        endPoint.PortNumber = int.Parse(strArray[2]);
                    }
                    else
                    {
                        handle = strArray.Length == 2 && strArray[0] == "establish" ? strArray[1] : throw new ApplicationException("Incorrect answer received");
                        flag = false;
                    }
                }
                catch (Exception ex)
                {
                    this.HandleError("Authentication failed for '{0}'", ex, (object)this.clientControlAddress_);
                    return;
                }
                if (flag)
                    this.StartListen(endPoint);
                else
                    this.EstablishGateLink(handle);
            }
            finally
            {
                TcpProxyManager.Instance.LeaveAsyncOperation();
            }
        }

        private void EstablishGateLink(string handle)
        {
            Socket socket = (Socket)null;
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ != null && ServerGateLink.clientSocket_.Handle.ToString() == handle)
                {
                    socket = ServerGateLink.clientSocket_;
                    ServerGateLink.clientSocket_ = (Socket)null;
                }
                else
                {
                    this.DisposeGateClientSocket();
                    this.HandleError("Gate link connection for handle '{0}' timed out", (Exception)null, (object)handle);
                    return;
                }
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
            try
            {
                this.EncryptAndSend("ok", this.clientControlSocket_);
                SocketPair socketPair = new SocketPair(socket, socket.RemoteEndPoint.ToString(), this.clientControlSocket_, this.clientControlAddress_);
                this.clientControlSocket_ = (Socket)null;
                socketPair.StartPairWork();
            }
            catch (Exception ex)
            {
                this.HandleError("Gate link connection for '{0}' failed", ex, (object)Tools.GetSafeRemoteEndPoint(socket));
                try
                {
                    if (socket.Connected)
                        socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch
                {
                }
            }
        }

        private void StartListen(Config.EndPoint endPoint)
        {
            Logger.Important("Remote command from '{0}'. Starting listening on '{1}'", (object)this.clientControlAddress_, (object)endPoint);
            try
            {
                this.listener_ = new TcpListener(IPAddress.Parse(endPoint.IP), endPoint.PortNumber);
                this.listener_.Start();
                this.EncryptAndSend("ok", this.clientControlSocket_);
            }
            catch (Exception ex1)
            {
                string str = string.Format("Can't listen on '{0}'. Details: {1}", (object)endPoint, (object)ex1.ToString());
                try
                {
                    this.EncryptAndSend(str, this.clientControlSocket_);
                }
                catch (Exception ex2)
                {
                    this.HandleError("Can't send answer to '{0}'", ex2, (object)this.clientControlAddress_);
                    return;
                }
                this.HandleError(str, (Exception)null);
                return;
            }
            this.controlLink_.AddServerGateLink(this);
            this.ProcessPending();
        }

        private void StartGateConnection(Socket cs)
        {
            try
            {
                byte[] buffer = this.Encrypt("connect " + cs.Handle.ToString() + " " + ((IPEndPoint)cs.RemoteEndPoint).Address.ToString());
                ServerGateLink.clientGateMutex_.WaitOne();
                try
                {
                    ServerGateLink.clientSocket_ = cs;
                    ServerGateLink.connectPendingUntil_ = DateTime.UtcNow.AddMilliseconds((double)this.keepAliveTimeout_);
                    TcpProxyManager.Instance.EnterAsyncOperation();
                    this.clientControlSocket_.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(this.ClientConnectedSent), (object)cs);
                }
                finally
                {
                    ServerGateLink.clientGateMutex_.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Can't establish gate link from '{0}' for client control '{1}'", ex, (object)Tools.GetSafeRemoteEndPoint(cs), (object)this.clientControlAddress_);
                this.DisposeGateClientSocket();
            }
        }

        private bool CheckPendingGateConnect()
        {
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ == null)
                    return false;
                if (!(ServerGateLink.connectPendingUntil_ < DateTime.UtcNow))
                    return true;
                this.DisposeGateClientSocket();
                return false;
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
        }

        private void DisposeGateClientSocket()
        {
            ServerGateLink.clientGateMutex_.WaitOne();
            try
            {
                if (ServerGateLink.clientSocket_ == null)
                    return;
                try
                {
                    if (ServerGateLink.clientSocket_.Connected)
                        ServerGateLink.clientSocket_.Shutdown(SocketShutdown.Both);
                    ServerGateLink.clientSocket_.Close();
                }
                catch
                {
                }
                ServerGateLink.clientSocket_ = (Socket)null;
            }
            finally
            {
                ServerGateLink.clientGateMutex_.ReleaseMutex();
            }
        }

        private void ClientConnectedSent(IAsyncResult ar)
        {
            if (this.isDisposed_)
                return;
            Socket asyncState = (Socket)ar.AsyncState;
            string str = "";
            try
            {
                str = Tools.GetSafeRemoteEndPoint(asyncState);
                this.clientControlSocket_.EndSend(ar);
                string andDecrypt = this.ReceiveAndDecrypt(this.clientControlSocket_);
                if (andDecrypt != "ok")
                    throw new ApplicationException("Failed with message: " + andDecrypt);
            }
            catch (Exception ex)
            {
                Logger.Error("Can't establish gate link from '{0}' for client control '{1}'", ex, (object)str, (object)this.clientControlAddress_);
                this.DisposeGateClientSocket();
            }
            TcpProxyManager.Instance.LeaveAsyncOperation();
        }

        private void HandleError(string Message, Exception ex, params object[] args)
        {
            Logger.Error(Message, ex, args);
            this.Dispose();
        }

        private void PingSent(IAsyncResult ar)
        {
            if (this.isDisposed_)
                return;
            try
            {
                try
                {
                    this.clientControlSocket_.EndSend(ar);
                }
                catch (Exception ex)
                {
                    this.HandleError("Can't send ping to '{0}'", ex, (object)this.clientControlAddress_);
                    return;
                }
                try
                {
                    if (this.ReceiveAndDecrypt(this.clientControlSocket_) != "pong")
                        throw new ApplicationException("Incorrect answer received");
                }
                catch (Exception ex)
                {
                    this.HandleError("RemoteGate client disconnected '{0}'", ex, (object)this.clientControlAddress_);
                }
            }
            finally
            {
                TcpProxyManager.Instance.LeaveAsyncOperation();
            }
        }

        public void Dispose()
        {
            this.isDisposed_ = true;
            if (this.clientControlSocket_ != null)
            {
                try
                {
                    if (this.clientControlSocket_.Connected)
                        this.clientControlSocket_.Shutdown(SocketShutdown.Both);
                    this.clientControlSocket_.Close();
                }
                catch
                {
                }
            }
            if (this.listener_ != null)
            {
                Logger.Important("Stopping listening on '{0}' [gate]", (object)this.listener_.LocalEndpoint);
                try
                {
                    this.listener_.Stop();
                }
                catch
                {
                }
            }
            this.controlLink_.RemoveServerGateLink(this);
        }
    }
