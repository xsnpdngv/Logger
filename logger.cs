// @file     logger.cs
// @brief    Simple logger library
// @details  Test excercise for Senior Developer job application to Nexogen
// @author   Tamas Dezso <dezso.t.tamas@gmail.com>
// @date     July 13, 2017
// @version  1.0
//
// Two types of loggers are available: Synchronous and Asynchronous.
// Both of them can be used with any of the three following log writers:
// Console, File and Stream.
//
// Sample usage:
//
//     var syncFileLogger = SyncLogger.Create(FileLogWriter.Create());
//     syncFileLogger.Debug("Test debug message");
//
//     var fileStream = new FileStream("asdf.txt", FileMode.Append);
//     var asyncStreamLogger =
//         AsyncLogger.Create(StreamLogWriter.Create(fileStream));
//     asyncStreamLogger.Error("Test error message");
//     asyncStreamLogger.Stop();
//
// Tested on:
//
//     macOS Sierra Version 10.12.5 (16F73)
//     Visual Studio Community 2017 for Mac Version 7.0.1 (build 24)

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;

namespace Nexogen.Logger.TDezso
{
    // ----- Logger interface -------------------------------------------------
    public interface ILogger
    {
        // Log messages at appropriate importance level
        void Debug(string message);
        void Info(string message);
        void Error(string message);

        // Logging level: messages are written only if their importance
        // level is equal to or greater than it
        LogLevel Level { get; set; }
    }

    // ----- Asynchronous logger interface extension --------------------------
    public interface IAsyncLogger : ILogger
    {
        // Safely stops log queue consumer thread after writing pending
        // entries out. New logs are discarded after stopping.
        void Stop();
    }

    // ----- Log writer interface ---------------------------------------------
    public interface ILogWriter
    {
        // Writes log to output
        void WriteLog(LogEntry logEntry);
    }

    // ----- Logging/message importance levels --------------------------------
    public enum LogLevel
    {
        Dbg, // Debugging
        Inf, // Information
        Err  // Error
    };

    // ----- Log entry structure ----------------------------------------------
    public struct LogEntry
    {
        // Fields of a log entry
        public string Message { get; private set; }
        public LogLevel Level { get; private set; }
        public DateTime Time  { get; private set; }

        // Constructor
        private LogEntry(LogLevel level, DateTime time, string message)
            { Level = level; Time = time; Message = message; }

        // Creates and fills new log entry structure
        public static LogEntry Create(LogLevel level, DateTime time,
                                      string message)
            { return new LogEntry(level, time, message); }

        // Formats log entry string
        public override string ToString() =>
            string.Format($"{Time.ToString(TimeFormat)} [{Level}] {Message}");

        // Defines format of time in log for writing
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
    }


    // ----- Base logger class (abstract) -------------------------------------
    abstract public class BaseLogger : ILogger
    {
        // Log messages at appropriate importance level
        public void Debug(string message) { Log(LogLevel.Dbg, message); }
        public void Info(string message)  { Log(LogLevel.Inf, message); }
        public void Error(string message) { Log(LogLevel.Err, message); }

        // Logs message if current logging level allows
        protected void Log(LogLevel level, string message)
        {
            // If log should be dispatched
            if (level >= Level)
                DispatchLog(LogEntry.Create(level, DateTime.Now, message));
        }

        // Dispatches log entry
        abstract protected void DispatchLog(LogEntry logEntry);

        // --- Attributes ---
        public LogLevel Level { get; set; } = LogLevel.Dbg;
        protected ILogWriter _logWriter;
    }

    // ----- Synchronous logger class -----------------------------------------
    public class SyncLogger : BaseLogger, ILogger
    {
        // Constructor
        private SyncLogger(ILogWriter logWriter)
            { _logWriter = logWriter; }

        // Creates new synchronous logger instance
        public static ILogger Create(ILogWriter logWriter)
            { return new SyncLogger(logWriter); }

        // Dispatches log entry by simply writing it
        override protected void DispatchLog(LogEntry logEntry)
            { _logWriter.WriteLog(logEntry); }
    }

    // NOTE: SyncLogger could be changed to asynchronous with one little
    // modification:
    //
    //  override protected void DispatchLog(LogEntry logEntry)
    //      { _Task.Factory.StartNew(() => _logWriter.WriteLog(logEntry)); }
    //
    // However, this would not guarantee the order-correctness of log entries
    // on the output side, since the tasks created for writing are not meant
    // to be accomplished in the exact order of starting.

    // ----- Asynchronous logger class ----------------------------------------
    public class AsyncLogger : BaseLogger, IAsyncLogger
    {
        // Constructor
        private AsyncLogger(ILogWriter logWriter)
        {
            _logWriter = logWriter;

            // Create log queue (FIFO)
            _logQueue = new BlockingCollection<LogEntry>();

            // Create and start log queue consumer thread
            _consumerThread = new Thread(LogQueueLoop)
            {
                Name = "LogQueueConsumer Thread",
                IsBackground = true
            };
            _consumerThread.Start();
        }

        // Creates new asynchronous logger instance
        public static IAsyncLogger Create(ILogWriter logWriter)
            { return new AsyncLogger(logWriter); }

        // Dispatches log entry by adding it to the log queue
        override protected void DispatchLog(LogEntry logEntry)
        {
            // If log queue accepts adding, add entry to it
            if (!_logQueue.IsAddingCompleted)
                _logQueue.Add(logEntry);
        }

        // Log queue handling loop - body of the consumer thread
        private void LogQueueLoop()
        {
            // If entry can be taken from log queue, write it out
            while (_logQueue.TryTake(out LogEntry logEntry, Timeout.Infinite))
                _logWriter.WriteLog(logEntry);
        }

        // Safely stops log queue consumer thread
        public void Stop()
        {
            // Mark queue as not accepting more additions. Attempts to take
            // from the queue will not wait when the collection is empty.
            _logQueue.CompleteAdding();

            // Wait for completion of the consumer thread
            // NOTE: cannot do this in destructor because of its thread
            _consumerThread.Join();
        }

        // --- Attributes ---
        private readonly BlockingCollection<LogEntry> _logQueue;
        private Thread _consumerThread;
    }


    // ----- Console log writer class -----------------------------------------
    public class ConsoleLogWriter : ILogWriter
    {
        // Constructor
        private ConsoleLogWriter() { }

        // Creates new console log writer instance
        public static ILogWriter Create()
            { return new ConsoleLogWriter(); }

        // Writes log entry to console
        public void WriteLog(LogEntry logEntry)
        {
            // Throw exception if message is too long
            if (logEntry.Message.Length >= MAX_MESSAGE_LENGTH)
                throw new ArgumentException("Log message length exceeds limit");

            // Ensure thread safety for color setting
            lock (_syncRoot)
            {
                // Set console color according to log level
                Console.ForegroundColor =
                    logEntry.Level == LogLevel.Dbg ? ConsoleColor.Gray  :
                    logEntry.Level == LogLevel.Inf ? ConsoleColor.Green :
                                            /* Err */ConsoleColor.Red;

                // Write log entry to console
                Console.WriteLine(logEntry.ToString());

                // Set console color back to original
                Console.ResetColor();
            }
        }

        // --- Attributes ---
        private const uint MAX_MESSAGE_LENGTH = 1000;
        private static readonly object _syncRoot = new Object();
    }

    // ----- File log writer class (singleton) --------------------------------
    public sealed class FileLogWriter : ILogWriter
    {
        // Constructor
        private FileLogWriter()
            { OpenFile(); }

        // Creates/gets the single file log writer instance
        public static ILogWriter Create()
            { return Instance(); }

        // Singleton instance handler
        public static ILogWriter Instance()
        {
            // Ensure thread safety
            lock (_syncRoot)
            {
                // Do not create more instances
                if (_instance == null)
                    _instance = new FileLogWriter();
                return _instance;
            }
        }

        // Opens log file to append
        private void OpenFile()
        {
            _fileInfo = new FileInfo($"{FILE_NAME}.{FILE_EXTENSION}");
            _file = _fileInfo.AppendText();

            // Flush buffer if anything is written into
            _file.AutoFlush = true;
        }

        // Writes log entry to file
        public void WriteLog(LogEntry logEntry)
        {
            // Ensure thread safety for file operations
            lock (_file)
            {
                _file.WriteLine(logEntry.ToString());

                // Check whether to rotate log file (and do if so)
                RotateFile();
            }
        }

        // Rotates log file by size. Archives file if it reaches size limit
        // and re-opens original one.
        private void RotateFile()
        {
            _fileInfo.Refresh();
            if (_fileInfo.Length >= FILE_SIZE_TO_ARCHIVE)
            {
                ArchiveFile();
                OpenFile();
            }
        }

        // Closes and renames file to the next (available) archive filename
        private void ArchiveFile()
        {
            // Close file before renaming
            _file.Close();

            // Generate next archive filename
            string archiveFileName;
            do {
                archiveFileName = $"{FILE_NAME}.{_nextNumber}.{FILE_EXTENSION}";
                ++_nextNumber;
            } while (File.Exists(archiveFileName));

            // Rename (archive) file
            _fileInfo.MoveTo(archiveFileName);
        }

        // --- Attributes ---
        private static volatile FileLogWriter _instance;
        private static readonly object _syncRoot = new Object();
        private StreamWriter _file;
        private FileInfo _fileInfo;
        private const string FILE_NAME = "log";
        private const string FILE_EXTENSION = "txt";
        private const uint FILE_SIZE_TO_ARCHIVE = 5 * 1024;
        private int _nextNumber = 1;
    }

    // ----- Stream log writer class ------------------------------------------
    public class StreamLogWriter : ILogWriter
    {
        // Constructor
        private StreamLogWriter(Stream stream)
        {
            // Wrap stream into a writer object
            var streamWriter = new StreamWriter(stream)
            {
                // Flush buffer if anything is written into
                AutoFlush = true
            };

            // Create a thread safe wrapper around the stream writer
            _textWriter = TextWriter.Synchronized(streamWriter);
        }

        // Creates new stream log writer instance
        public static ILogWriter Create(Stream stream)
            { return new StreamLogWriter(stream); }

        // Writes log entry to stream
        public void WriteLog(LogEntry logEntry)
            { _textWriter.WriteLine(logEntry.ToString()); }

        // --- Attributes ---
        private readonly TextWriter _textWriter;
    }


    // ----- Logger/Log writer tester class -----------------------------------
    public static class LoggerTest
    {
        public static void Run()
        {
            // Logger tests
            SyncLoggerTests();
            AsyncLoggerTests();
        }

        // Synchronous logger tests
        private static void SyncLoggerTests()
        {
            // Synchronous logger tests
            SyncConsoleLoggerTests();
            SyncFileLoggerTests();
            SyncStreamLoggerTests();
        }

        private static void AsyncLoggerTests()
        {
            // Asynchronous logger tests
            AsyncConsoleLoggerTests();
            AsyncFileLoggerTests();
            AsyncStreamLoggerTests();
        }

        // Synchronous console logger tests
        private static void SyncConsoleLoggerTests()
        {
            var scl = SyncLogger.Create(ConsoleLogWriter.Create());
            SuiteDbg(scl);
            SuiteInf(scl);
            SuiteErr(scl);
        }

        // Synchronous file logger tests
        private static void SyncFileLoggerTests()
        {
            var sfl = SyncLogger.Create(FileLogWriter.Create());
            SuiteBulk(sfl);
        }

        // Synchronous stream logger tests
        private static void SyncStreamLoggerTests()
        {
            var fs = new FileStream("stream_s.txt", FileMode.Append);
            var ssl = SyncLogger.Create(StreamLogWriter.Create(fs));
            SuiteBulk(ssl);
            fs.Close();
        }

        // Asynchronous console logger tests
        private static void AsyncConsoleLoggerTests()
        {
            var acl = AsyncLogger.Create(ConsoleLogWriter.Create());
            SuiteDbg(acl);
            SuiteInf(acl);
            SuiteErr(acl);
            acl.Stop();
            SuiteDbg(acl);
        }

        // Asynchronous file logger tests
        private static void AsyncFileLoggerTests()
        {
            var afl = AsyncLogger.Create(FileLogWriter.Create());
            SuiteBulk(afl);
            afl.Stop();
            SuiteBulk(afl);
        }

        // Asynchronous stream logger tests
        private static void AsyncStreamLoggerTests()
        {
            var fs = new FileStream("stream_a.txt", FileMode.Append);
            var asl = AsyncLogger.Create(StreamLogWriter.Create(fs));
            SuiteBulk(asl);
            asl.Stop();
            SuiteBulk(asl);
        }

        // Debug level test suite
        private static void SuiteDbg(ILogger logr)
        {
            logr.Level = LogLevel.Dbg;
            logr.Debug("Suite #1 test dbg level message");
            logr.Info("Suite #1 test inf level message");
            logr.Error("Suite #1 test err level message");
        }

        // Information level test suite
        private static void SuiteInf(ILogger logr)
        {
            logr.Level = LogLevel.Inf;
            logr.Debug("Suite #2 test dbg level message (NOT to be logged)");
            logr.Info("Suite #2 test inf level message");
            logr.Error("Suite #2 test err level message");
        }

        // Error level test suite
        private static void SuiteErr(ILogger logr)
        {
            logr.Level = LogLevel.Err;
            logr.Debug("Suite #3 test dbg level message (NOT to be logged)");
            logr.Info("Suite #3 test inf level message (NOT to be logged)");
            logr.Error("Suite #3 test err level message");
        }

        // Bulk logging test suite (debub level)
        private static void SuiteBulk(ILogger logr)
        {
            for (int i = 0; i < 100; ++i)
            {
                logr.Level = LogLevel.Dbg;
                logr.Debug($"Suite #4 test dbg level message: {i}");
                logr.Info($"Suite #4 test inf level message: {i}");
                logr.Error($"Suite #4 test err level message: {i}");
            }
        }
    }
}
