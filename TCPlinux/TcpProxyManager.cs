using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using Config;
namespace TCPlinux;

public class TcpProxyManager
{
    private Thread manageThread_;
        private ManualResetEvent stopEvent_ = new ManualResetEvent(false);
        private AutoResetEvent configChangedEvent_ = new AutoResetEvent(false);
        private LinkConfig linkConfig_;
        private string linkConfigFileName_ = "LinkConfig.xml";
        private FileSystemWatcher linkConfigWatcher_;
        private DateTime LastConfigUpdated = DateTime.MinValue;
        private List<SimpleLink> simpleLinks_ = new List<SimpleLink>();
        private ServerControlLink serverControlLink_;
        private List<RemoteGateLink> remoteGateLink_ = new List<RemoteGateLink>();
        private int operationCount_;
        private static TcpProxyManager instance_;
        //private string exeConfigurationPath = "C:\\Users\\Lenovo\\OneDrive\Рабочий стол\diploma\TCPlinux\TCPlinux\bin\Debug\net6.0";
        public WaitHandle StopHandle => (WaitHandle)this.stopEvent_;

        public void Run(bool wait)
        {
            manageThread_ = new Thread(new ParameterizedThreadStart(DoWork));
            //Configuration conf = ConfigurationManager.OpenExeConfiguration(exeConfigurationPath);
            //string PropValue = conf.AppSettings.Settings["LinkConfig"].Value;
            linkConfigFileName_ = "LinkConfig.xml";
            linkConfigWatcher_ = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, linkConfigFileName_);
            linkConfigWatcher_.NotifyFilter = NotifyFilters.LastWrite;
            linkConfigWatcher_.Changed += new FileSystemEventHandler(OnChanged);
            linkConfigWatcher_.EnableRaisingEvents = true;
            LoadConfiguration();
            manageThread_.Start();
            if (!wait)
                return;
            manageThread_.Join();
        }

        public void Stop()
        {
            this.stopEvent_.Set();
            if (this.manageThread_.ThreadState != ThreadState.Running)
                return;
            this.manageThread_.Join(60000);
            if (this.manageThread_.ThreadState != ThreadState.Running)
                return;
            Logger.Error("Working thread is not finished. Operations active: '{0}'. Aborting.", (object)this.operationCount_);
            this.manageThread_.Abort();
        }

        public void DoWork(object data)
        {
            try
            {
                StartSockets();
            label_1:
                while (true)
                {
                    switch (WaitHandle.WaitAny(new WaitHandle[2]
                    {
            (WaitHandle) stopEvent_,
            (WaitHandle) configChangedEvent_
                    }, 500, false))
                    {
                        case 0:
                            goto label_19;
                        case 1:
                            LoadConfiguration();
                            StartSockets();
                            continue;
                        default:
                            goto label_3;
                    }
                }
            label_3:
                if (serverControlLink_ != null)
                    serverControlLink_.ProcessPending();
                foreach (SimpleLink simpleLink in this.simpleLinks_)
                    simpleLink.ProcessPending();
                using (List<RemoteGateLink>.Enumerator enumerator = remoteGateLink_.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                        enumerator.Current.ProcessPending();
                    goto label_1;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception occured", ex);
            }
            finally
            {
                Logger.Important("Shutting down...");
                ShutDown();
            }
        label_19:
            while (operationCount_ > 0)
            {
                Thread.Sleep(100);
                Thread.MemoryBarrier();
                Logger.Debug("waiting for " + (object)operationCount_ + " operations to complete...");
            }
        }

        public void EnterAsyncOperation() => Interlocked.Increment(ref this.operationCount_);

        public void LeaveAsyncOperation() => Interlocked.Decrement(ref this.operationCount_);

        private void ShutDown()
        {
            this.linkConfigWatcher_.Dispose();
            this.linkConfigWatcher_ = (FileSystemWatcher)null;
            this.StopSockets();
        }

        private void StopSockets()
        {
            foreach (SimpleLink simpleLink in this.simpleLinks_)
            {
                try
                {
                    simpleLink.Dispose();
                }
                catch
                {
                }
            }
            simpleLinks_.Clear();
            if (serverControlLink_ != null)
            {
                try
                {
                    serverControlLink_.Dispose();
                }
                catch
                {
                }
                serverControlLink_ = (ServerControlLink)null;
            }
            foreach (RemoteGateLink remoteGateLink in this.remoteGateLink_)
            {
                try
                {
                    remoteGateLink.Dispose();
                }
                catch
                {
                }
            }
            remoteGateLink_.Clear();
            for (int index = 0; index < 10; ++index)
            {
                if (operationCount_ > 0)
                {
                    Thread.Sleep(100);
                    Thread.MemoryBarrier();
                    Logger.Debug("waiting for " + (object)operationCount_ + " operations to complete...");
                }
            }
        }

        private void StartSockets()
        {
            StopSockets();
            foreach (Config.SimpleLink simpleLink in linkConfig_.SimpleLinks)
                simpleLinks_.Add(new SimpleLink(linkConfig_.BindTimeout, simpleLink));
            if (linkConfig_.ServerControlLink != null)
                serverControlLink_ = new ServerControlLink(linkConfig_.BindTimeout, linkConfig_.KeepAliveTimeout, linkConfig_.ServerControlLink);
            foreach (Config.RemoteGateLink remoteGateLink in linkConfig_.RemoteGateLinks)
                remoteGateLink_.Add(new RemoteGateLink(linkConfig_.BindTimeout, linkConfig_.KeepAliveTimeout, remoteGateLink));
        }

        private void LoadConfiguration()
        {
            try
            {
                using (TextReader textReader = (TextReader)new StreamReader(AppDomain.CurrentDomain.BaseDirectory + this.linkConfigFileName_, Encoding.UTF8))
                {
                    this.linkConfig_ = (LinkConfig)new XmlSerializer(typeof(LinkConfig)).Deserialize(textReader);
                    Logger.LogLevel = (LogLevel)this.linkConfig_.LogLevel;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Can't load LinkConfig", ex);
                this.linkConfig_ = new LinkConfig();
            }
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(e.FullPath);
            if (!(lastWriteTimeUtc > LastConfigUpdated))
                return;
            LastConfigUpdated = lastWriteTimeUtc;
            Logger.Important("Configuration updated on {0}. Reloading...", (object)LastConfigUpdated);
            configChangedEvent_.Set();
        }

        public static TcpProxyManager Instance
        {
            get
            {
                if (TcpProxyManager.instance_ == null)
                    TcpProxyManager.instance_ = new TcpProxyManager();
                return TcpProxyManager.instance_;
            }
        }
    }
