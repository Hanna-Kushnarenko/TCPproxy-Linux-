using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using Config;
using System.Security.Cryptography;
using System.Net;
namespace TCPlinux;

    public class RemoteGateLink : CryptoLink, IDisposable
    {

        private Config.RemoteGateLink linkConfig_;
        private Socket server_;
        private Socket serverCandidate_;
        private int connectTimeout_;
        private DateTime lastConnectTry_;
        private DateTime lastReceived_;
        private Mutex lastReceivedMutex_;
        private int keepAliveTimeout_;
        private bool isDisposed_;
        private byte[] buffer;

        public RemoteGateLink(int connectTimeout, int keepAliveTimeout, Config.RemoteGateLink linkConfig)
        {
            this.lastConnectTry_ = DateTime.MinValue;
            this.lastReceived_ = DateTime.MinValue;
            this.lastReceivedMutex_ = new Mutex();
            this.keepAliveTimeout_ = 0xea60;
            this.buffer = new byte[0x400];
            this.linkConfig_ = linkConfig;
            this.connectTimeout_ = connectTimeout;
            this.keepAliveTimeout_ = keepAliveTimeout;
            this.ProcessPending();
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            if (!this.isDisposed_)
            {
                try
                {
                    this.serverCandidate_.EndConnect(ar);
                    TcpProxyManager.Instance.EnterAsyncOperation();
                    this.serverCandidate_.BeginReceive(this.buffer, 0, 0x10, SocketFlags.None, new AsyncCallback(this.ReceiveCandidateCallback), this.serverCandidate_);
                }
                catch (Exception exception)
                {
                    object[] args = new object[] { Tools.GetSafeRemoteEndPoint(this.serverCandidate_) };
                    Logger.Error("Can't connect to remote gate control '{0}'", exception, args);
                    try
                    {
                        if (this.serverCandidate_.Connected)
                        {
                            this.serverCandidate_.Shutdown(SocketShutdown.Both);
                        }
                        this.serverCandidate_.Close();
                    }
                    catch
                    {
                    }
                    this.serverCandidate_ = null;
                    this.server_ = null;
                }
                finally
                {
                    TcpProxyManager.Instance.LeaveAsyncOperation();
                }
            }
        }

        public void Dispose()
        {
            this.isDisposed_ = true;
            if (this.server_ != null)
            {
                try
                {
                    if (this.server_.Connected)
                    {
                        this.server_.Shutdown(SocketShutdown.Both);
                    }
                    this.server_.Close();
                }
                catch
                {
                }
                this.server_ = null;
            }
            if (this.serverCandidate_ != null)
            {
                try
                {
                    if (this.serverCandidate_.Connected)
                    {
                        this.serverCandidate_.Shutdown(SocketShutdown.Both);
                    }
                    this.serverCandidate_.Close();
                }
                catch
                {
                }
                this.serverCandidate_ = null;
            }
        }

        private void EstablishGateLink(string handle, string ipaddress)
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket socket2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                object[] args = new object[] { this.linkConfig_.ServiceAddress };
                Logger.Info("Connecting to service '{0}'", args);
                try
                {
                    socket2.Connect(this.linkConfig_.ServiceAddress.Address, this.linkConfig_.ServiceAddress.PortNumber);
                }
                catch (Exception exception)
                {
                    base.EncryptAndSend("Connect to service failed: " + exception.Message, this.server_);
                    throw;
                }
                base.EncryptAndSend("ok", this.server_);
                object[] objArray2 = new object[] { this.linkConfig_.GateControlAddress, handle };
                Logger.Info("Connecting to remote link '{0}'. Handle '{1}'", objArray2);
                s.Connect(this.linkConfig_.GateControlAddress.Address, this.linkConfig_.GateControlAddress.PortNumber);
                byte[] buffer = new byte[0x10];
                if (s.Receive(buffer) != buffer.Length)
                {
                    throw new ApplicationException("Invalid answer received");
                }
                CryptoLink link = new CryptoLink();
                if (!string.IsNullOrEmpty(this.linkConfig_.Key))
                {
                    RijndaelManaged managed = new RijndaelManaged();
                    managed.Padding = PaddingMode.None;
                    managed.IV = buffer;
                    managed.Key = Tools.GenerateKey(this.linkConfig_.Key);
                    link.Decryptor = managed.CreateDecryptor();
                    link.Encryptor = managed.CreateEncryptor();
                }
                link.EncryptAndSend("establish " + handle, s);
                string str = link.ReceiveAndDecrypt(s);
                if (str != "ok")
                {
                    throw new ApplicationException("Invalid answer received:" + str);
                }
                new SocketPair(s, ipaddress + "<->" + s.RemoteEndPoint.ToString(), socket2, socket2.RemoteEndPoint.ToString()).StartPairWork();
            }
            catch (Exception exception2)
            {
                object[] args = new object[] { this.linkConfig_.GateControlAddress, this.linkConfig_.ServiceAddress, handle };
                Logger.Error("Can't establish link from remote link '{0}' to service '{1}'. Handle '{2}'", exception2, args);
                try
                {
                    if (socket2.Connected)
                    {
                        socket2.Shutdown(SocketShutdown.Both);
                    }
                    socket2.Close();
                }
                catch
                {
                }
                try
                {
                    if (s.Connected)
                    {
                        s.Shutdown(SocketShutdown.Both);
                    }
                    s.Close();
                }
                catch
                {
                }
            }
        }

        private void HandleError(string Message, Exception ex, params object[] args)
        {
            Logger.Error(Message, ex, args);
            this.Dispose();
        }

        public void ProcessPending()
        {
            if ((this.server_ == null) && (this.lastConnectTry_.AddMilliseconds((double)this.connectTimeout_) < DateTime.UtcNow))
            {
                if (this.serverCandidate_ != null)
                {
                    try
                    {
                        this.serverCandidate_.Shutdown(SocketShutdown.Both);
                        this.serverCandidate_.Close();
                    }
                    catch
                    {
                    }
                    this.serverCandidate_ = null;
                }
                this.lastConnectTry_ = DateTime.UtcNow;
                this.serverCandidate_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                object[] args = new object[] { this.linkConfig_.GateControlAddress };
                Logger.Important("Connecting to remote link gateway control port '{0}'", args);
                TcpProxyManager.Instance.EnterAsyncOperation();
                this.serverCandidate_.BeginConnect(this.linkConfig_.GateControlAddress.Address, this.linkConfig_.GateControlAddress.PortNumber, new AsyncCallback(this.ConnectCallback), null);
            }
            if (this.server_ != null)
            {
                this.lastReceivedMutex_.WaitOne();
                try
                {
                    if (this.lastReceived_.AddMilliseconds((double)this.keepAliveTimeout_) < DateTime.UtcNow)
                    {
                        try
                        {
                            object[] args = new object[] { this.linkConfig_.GateControlAddress, this.lastReceived_.ToLocalTime() };
                            Logger.Info("Connection to remote link gateway control port timed out '{0}'. Last response on: '{1}'", args);
                            this.server_.Shutdown(SocketShutdown.Both);
                            this.server_.Close();
                        }
                        catch
                        {
                        }
                        this.server_ = null;
                    }
                }
                finally
                {
                    this.lastReceivedMutex_.ReleaseMutex();
                }
            }
        }


        private void ReceiveCallback(IAsyncResult ar)
        {
            if (!this.isDisposed_)
            {
                Socket asyncState = (Socket)ar.AsyncState;
                try
                {
                    int length = asyncState.EndReceive(ar);
                    if (length != 0)
                    {
                        this.lastReceivedMutex_.WaitOne();
                        try
                        {
                            this.lastReceived_ = DateTime.UtcNow;
                        }
                        finally
                        {
                            this.lastReceivedMutex_.ReleaseMutex();
                        }
                        string str = base.Decrypt(this.buffer, length);
                        if (str == "ping")
                        {
                            base.EncryptAndSend("pong", asyncState);
                        }
                        else
                        {
                            string[] strArray = str.Split(new char[] { ' ' });
                            if ((strArray.Length != 3) || (strArray[0] != "connect"))
                            {
                                throw new ApplicationException("Invalid answer: " + str);
                            }
                            string handle = strArray[1];
                            string ipString = strArray[2];
                            if (SourcePoint.IsInside(IPAddress.Parse(ipString), this.linkConfig_.Sources))
                            {
                                this.EstablishGateLink(handle, ipString);
                            }
                            else
                            {
                                base.EncryptAndSend("Unauthorized access from '" + ipString + "'", asyncState);
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(100);
                        Thread.MemoryBarrier();
                        if (this.server_ != null)
                        {
                            throw new ApplicationException("No bytes returned from the endpoint");
                        }
                        return;
                    }
                }
                catch (Exception exception1)
                {
                    object[] args = new object[] { Tools.GetSafeRemoteEndPoint(asyncState), exception1.ToString() };
                    Logger.Error("Can't receive from remote gate control '{0}'. Details: {1}", args);
                    try
                    {
                        if (this.server_.Connected)
                        {
                            this.server_.Shutdown(SocketShutdown.Both);
                        }
                        this.server_.Close();
                    }
                    catch
                    {
                    }
                    this.server_ = null;
                    return;
                }
                finally
                {
                    TcpProxyManager.Instance.LeaveAsyncOperation();
                }
                TcpProxyManager.Instance.EnterAsyncOperation();
                asyncState.BeginReceive(this.buffer, 0, this.buffer.Length, SocketFlags.None, new AsyncCallback(this.ReceiveCallback), asyncState);
            }
        }


        private void ReceiveCandidateCallback(IAsyncResult ar)
        {
            if (!this.isDisposed_)
            {
                try
                {
                    if (this.serverCandidate_.EndReceive(ar) != 0x10)
                    {
                        throw new ApplicationException("Invalid answer received");
                    }
                    byte[] destinationArray = new byte[0x10];
                    Array.Copy(this.buffer, destinationArray, destinationArray.Length);
                    if (!string.IsNullOrEmpty(this.linkConfig_.Key))
                    {
                        RijndaelManaged managed = new RijndaelManaged();
                        managed.Padding = PaddingMode.None;
                        managed.IV = destinationArray;
                        managed.Key = Tools.GenerateKey(this.linkConfig_.Key);
                        base.Decryptor = managed.CreateDecryptor();
                        base.Encryptor = managed.CreateEncryptor();
                    }
                    object[] objArray = new object[] { "listen ", this.linkConfig_.GateEndPoint.IP, " ", this.linkConfig_.GateEndPoint.PortNumber };
                    string msg = string.Concat(objArray);
                    base.EncryptAndSend(msg, this.serverCandidate_);
                    string message = base.ReceiveAndDecrypt(this.serverCandidate_);
                    if (message != "ok")
                    {
                        throw new ApplicationException(message);
                    }
                    this.lastReceivedMutex_.WaitOne();
                    try
                    {
                        this.server_ = this.serverCandidate_;
                        this.serverCandidate_ = null;
                        this.lastReceived_ = DateTime.UtcNow;
                    }
                    finally
                    {
                        this.lastReceivedMutex_.ReleaseMutex();
                    }
                }
                catch (Exception exception)
                {
                    object[] args = new object[] { Tools.GetSafeRemoteEndPoint(this.serverCandidate_) };
                    Logger.Error("Can't start listening on the remote gate control '{0}'", exception, args);
                    try
                    {
                        if (this.serverCandidate_.Connected)
                        {
                            this.serverCandidate_.Shutdown(SocketShutdown.Both);
                        }
                        this.serverCandidate_.Close();
                    }
                    catch
                    {
                    }
                    this.serverCandidate_ = null;
                    this.server_ = null;
                    return;
                }
                finally
                {
                    TcpProxyManager.Instance.LeaveAsyncOperation();
                }
                TcpProxyManager.Instance.EnterAsyncOperation();
                this.server_.BeginReceive(this.buffer, 0, this.buffer.Length, SocketFlags.None, new AsyncCallback(this.ReceiveCallback), this.server_);
            }
        }



    }
