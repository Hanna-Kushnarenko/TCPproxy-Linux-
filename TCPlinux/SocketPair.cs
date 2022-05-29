using System.Net.Sockets;
namespace TCPlinux;

public class SocketPair : IDisposable
    {
        // Fields
        private Socket s1_;
        private Socket s2_;
        private string s1info_;
        private string s2info_;
        private Thread workerThread_;
        public Socket s1 => this.s1_;

        public Socket s2 => this.s2_;
        public SocketPair(Socket s1, string s1info, Socket s2, string s2info)
        {
            this.s1_ = s1;
            this.s1info_ = s1info;
            this.s2_ = s2;
            this.s2info_ = s2info;
        }
        public void Dispose()
        {
            try
            {
                if (this.s1.Connected)
                {
                    this.s1.Shutdown(SocketShutdown.Both);
                }
                this.s1.Close();
            }
            catch
            {
            }
            try
            {
                if (this.s2.Connected)
                {
                    this.s2.Shutdown(SocketShutdown.Both);
                }
                this.s2.Close();
            }
            catch
            {
            }
        }



        private void DoPairWork()
        {
            object[] args = new object[] { this.s1info_, this.s2info_ };
            Logger.Info("Paired {0}<->{1}", args);
            try
            {
                byte[] buf = new byte[4096];
                while (!TcpProxyManager.Instance.StopHandle.WaitOne(0, false))
                {
                    List<Socket> Received = new List<Socket>();
                    Received.Add(this.s1);
                    Received.Add(this.s2);
                    Socket.Select((System.Collections.IList)Received, (System.Collections.IList)null, (System.Collections.IList)null, 500);
                    if (Received.Count > 0 && (!SocketPair.Receive(Received, buf, this.s1, this.s1info_, this.s2, this.s2info_) || !SocketPair.Receive(Received, buf, this.s2, this.s2info_, this.s1, this.s1info_)))
                        break;


                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception occured while communicating '{0}<->{1}'", ex, (object)this.s1info_, (object)this.s2info_);
            }
            finally
            {
                this.Dispose();
                TcpProxyManager.Instance.LeaveAsyncOperation();
                Logger.Info("... Unpaired '{0}<->{1}'", (object)this.s1info_, (object)this.s2info_);
            }
        }

        private static void HandleError(Exception ex, string operation, string info_)
        {
            object[] args = new object[] { operation, info_ };
            Logger.Error("{0} '{1}' failed", ex, args);
        }

        private static bool Receive(
      List<Socket> Received,
      byte[] buf,
      Socket a,
      string ainfo,
      Socket b,
      string binfo)
        {
            int size = 0;
            try
            {
                if (a.Available > 0)
                {
                    size = a.Receive(buf);
                    if (size == 0)
                        return false;
                }
                else if (Received.Contains(a))
                    return false;
            }
            catch (Exception ex)
            {
                SocketPair.HandleError(ex, "Receive from", ainfo);
                return false;
            }
            if (size > 0)
            {
                try
                {
                    b.Send(buf, size, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    SocketPair.HandleError(ex, "Send to", binfo);
                    return false;
                }
            }
            return true;
        }


        public void StartPairWork()
        {
            TcpProxyManager.Instance.EnterAsyncOperation();
            this.workerThread_ = new Thread(new ThreadStart(this.DoPairWork));
            this.workerThread_.Start();
        }


    }
