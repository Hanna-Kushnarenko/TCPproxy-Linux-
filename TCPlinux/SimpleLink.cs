using System.Net.Sockets;
using System.Net;
using Config;
namespace TCPlinux;


     public class SimpleLink : IDisposable
    {
        // Fields
        private Config.SimpleLink linkConfig_;
        private TcpListener listener_;
        private int bindTimeout_;
        private DateTime lastListenTry_ = DateTime.MinValue;
        private bool listening_;
        private bool isDisposed_;

        public void ProcessPending()
        {
            if (this.listening_)
            {
                while (this.listener_.Pending())
                {
                    try
                    {
                        Socket s1 = this.listener_.AcceptSocket();
                        IPEndPoint remoteEndPoint = s1.RemoteEndPoint as IPEndPoint;
                        if (!SourcePoint.IsInside(remoteEndPoint.Address, this.linkConfig_.Sources))
                        {
                            Logger.Warning("Unauthorized connection to '{0}' from '{1}' closed", (object)this.linkConfig_.EndPoint, (object)remoteEndPoint);
                            s1.Shutdown(SocketShutdown.Both);
                            s1.Close();
                        }
                        else
                        {
                            Socket s2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            SocketPair socketPair = new SocketPair(s1, s1.RemoteEndPoint.ToString(), s2, this.linkConfig_.ServiceAddress.ToString());
                            TcpProxyManager.Instance.EnterAsyncOperation();
                            s2.BeginConnect(this.linkConfig_.ServiceAddress.Address, this.linkConfig_.ServiceAddress.PortNumber, new AsyncCallback(this.ConnectCallback), (object)socketPair);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Can't accept socket on '{0}'", ex, (object)this.linkConfig_.EndPoint);
                    }
                }
            }
            else
            {
                if (!(this.lastListenTry_.AddMilliseconds((double)this.bindTimeout_) < DateTime.UtcNow))
                    return;
                try
                {
                    this.lastListenTry_ = DateTime.UtcNow;
                    Logger.Important("Listening on '{0}' [simple link]", (object)this.linkConfig_.EndPoint);
                    this.listener_.Start();
                    this.listening_ = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("Can't listen on '{0}'", ex, (object)this.linkConfig_.EndPoint);
                }
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            if (this.isDisposed_)
                return;
            SocketPair asyncState = (SocketPair)ar.AsyncState;
            try
            {
                asyncState.s2.EndConnect(ar);
                asyncState.StartPairWork();
            }
            catch (Exception ex)
            {
                asyncState.Dispose();
                Logger.Error("Can't connect to '{0}'", ex, (object)this.linkConfig_.ServiceAddress);
            }
            TcpProxyManager.Instance.LeaveAsyncOperation();
        }

        public SimpleLink(int bindTimeout, Config.SimpleLink linkConfig)
        {
            this.linkConfig_ = linkConfig;
            this.listener_ = new TcpListener(IPAddress.Parse(linkConfig.EndPoint.IP), linkConfig.EndPoint.PortNumber);
            this.listener_.ExclusiveAddressUse = false;
            this.bindTimeout_ = bindTimeout;
            this.ProcessPending();
        }

        public void Dispose()
        {
            this.isDisposed_ = true;
            if (!this.listening_)
                return;
            Logger.Important("Stopping listening on '{0}' [simple link]", (object)this.linkConfig_.EndPoint);
            this.listening_ = false;
            try
            {
                this.listener_.Stop();
            }
            catch
            {
            }
        }




    }
