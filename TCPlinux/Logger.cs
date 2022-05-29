using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft;
using System.Diagnostics;
namespace TCPlinux;

public class Logger
{
    private static LogLevel k__BackingField;
        public static LogLevel LogLevel
        {
            get
            {
                return k__BackingField;
            }
            set
            {
               k__BackingField = value;
            }
        }

        public static void Debug(string Message, params object[] args)
        {
            Write(LogLevel.Debug, Message, args);
        }

        public static void Error(string Message, params object[] args)
        {
            Write(LogLevel.Error, Message, args);
        }
        public static void Error(string Message, Exception Ex, params object[] args)
        {
            if (Ex == null)
            {
                Error(Message, args);
            }
            else
            {
                Error(string.Format("{0}. Details: {1}", Message, Ex.ToString()), args);
            }
        }
        private static EventLogEntryType GetEventLogType(LogLevel Level)
        {
            switch (Level)
            {
                case LogLevel.Warning:
                    return EventLogEntryType.Warning;

                case LogLevel.Error:
                    return EventLogEntryType.Error;
            }
            return EventLogEntryType.Information;
        }


        public static void Important(string Message, params object[] args)
        {
            Write(LogLevel.Important, Message, args);
        }


        public static void Info(string Message, params object[] args)
        {
            Write(LogLevel.Info, Message, args);
        }
        public static void Warning(string Message, params object[] args)
        {
            Write(LogLevel.Warning, Message, args);
        }
        public static void Write(LogLevel Level, string Message, params object[] args)
        {
            string str = string.Format(Message, args);
            Console.WriteLine(str);
            if (Level >= LogLevel)
            {
                EventLog.WriteEntry("TcpProxy", str, GetEventLogType(Level));
            }
        }

}