using System.Net.Sockets;
using System.Net;
using Config;

namespace TCPlinux;

public class ServerControlLink
{
     // Fields
        private Config.ServerControlLink linkConfig_;
        private TcpListener listener_;
        private int bindTimeout_;
        private int keepAliveTimeout_;
        private DateTime lastListenTry_;
        private bool listening_;
        private List<ServerGateLink> serverGateLinks = new List<ServerGateLink>();
        private byte[] key_;
        public byte[] Key => this.key_;

        public void ProcessPending()
        {
            if (this.listening_)
            {
                while (this.listener_.Pending())
                {
                    try
                    {
                        Socket clientSocket = this.listener_.AcceptSocket();
                        IPEndPoint remoteEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                        if (!SourcePoint.IsInside(remoteEndPoint.Address, this.linkConfig_.Sources))
                        {
                            Logger.Warning("Unauthorized connection to '{0}' from '{1}' closed", (object)this.linkConfig_.EndPoint, (object)remoteEndPoint);
                            clientSocket.Shutdown(SocketShutdown.Both);
                            clientSocket.Close();
                        }
                        else
                        {
                            ServerGateLink serverGateLink = new ServerGateLink(this, clientSocket, this.keepAliveTimeout_, this.linkConfig_.ClientsAllowed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Can't accept socket on '{0}'", ex, (object)this.linkConfig_.EndPoint);
                    }
                }
            }
            else if (this.lastListenTry_.AddMilliseconds((double)this.bindTimeout_) < DateTime.UtcNow)
            {
                try
                {
                    this.lastListenTry_ = DateTime.UtcNow;
                    Logger.Important("Listening on '{0}' [control]", (object)this.linkConfig_.EndPoint);
                    try
                    {
                        this.listener_.Start();
                        this.listening_ = true;
                    }
                    catch
                    {
                    }
                    if (!this.listening_)
                    {
                        Thread.Sleep(300);
                        this.listener_.Start();
                        this.listening_ = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Can't listen on '{0}'", ex, (object)this.linkConfig_.EndPoint);
                }
            }
            foreach (ServerGateLink copyServerGateLink in this.GetSafeCopyServerGateLinks())
            {
                copyServerGateLink.ProcessPending();
            }
        }
        public ServerControlLink(int bindTimeout, int keepAliveTimeout, Config.ServerControlLink linkConfig)
        {
            this.linkConfig_ = linkConfig;
            this.listener_ = new TcpListener(IPAddress.Parse(linkConfig.EndPoint.IP), linkConfig.EndPoint.PortNumber);
            this.listener_.ExclusiveAddressUse = false;
            this.bindTimeout_ = bindTimeout;
            this.keepAliveTimeout_ = keepAliveTimeout;
            this.key_ = Tools.GenerateKey(linkConfig.Key);
            this.ProcessPending();
        }


        public void AddServerGateLink(ServerGateLink sgl)
        {
            TcpProxyManager.Instance.EnterAsyncOperation();
            Monitor.Enter((object)this.serverGateLinks);
            try
            {
                this.serverGateLinks.Add(sgl);
            }
            finally
            {
                Monitor.Exit((object)this.serverGateLinks);
            }
        }
        public void Dispose()
        {
            if (this.listening_)
            {
                Logger.Important("Stopping listening on '{0}' [control]", (object)this.linkConfig_.EndPoint);
                this.listening_ = false;
                try
                {
                    this.listener_.Stop();
                }
                catch
                {
                }
            }
            foreach (ServerGateLink copyServerGateLink in this.GetSafeCopyServerGateLinks())
                copyServerGateLink.Dispose();
        }



        private ServerGateLink[] GetSafeCopyServerGateLinks()
        {
            Monitor.Enter((object)this.serverGateLinks);
            try
            {
                return this.serverGateLinks.ToArray();
            }
            finally
            {
                Monitor.Exit((object)this.serverGateLinks);
            }
        }


        public void RemoveServerGateLink(ServerGateLink sgl)
        {
            Monitor.Enter((object)this.serverGateLinks);
            try
            {
                this.serverGateLinks.Remove(sgl);
            }
            finally
            {
                Monitor.Exit((object)this.serverGateLinks);
            }
            TcpProxyManager.Instance.LeaveAsyncOperation();
        }


}