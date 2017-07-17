# Logger library 

Create a simple logger library which can be used in other projects to log different messages.
The type  of the logger should be easily changed in every project.

There are three different message levels:

* debug
* info
* error

There are three types of the loggers:

* console logger: logs to the console
* file logger: logs to a file
* stream logger: logs to any stream

The console logger should throw an appropriate exception if the log message is longer than 1000 characters.
The console logger should set the color of the text depending on the message level:

* debug - gray
* info - green
* error - red

The file logger should rotate the files by size. If a logfile reaches the size of 5k it should be archived with the name `#{LogFileName}.#NextNumber.#{LogFileExtension}` and the logging should be continued with the original filename.
E.g.: original log name is: log.txt. The first rotation should create log.1.txt, the second rotation creates the log.2.txt file.

Every logger uses the same log formatting: `#{LogTime} [#{LogLevel}] #{LogMessage}`

## Optional (advanced) features

* Asyncronous logging, the logger should not block the thread which do the logging
* Make a package from this library so one can add (reference) the project easily.
