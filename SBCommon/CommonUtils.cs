using System;
using System.Collections.Generic;

namespace SBCommon
{
    public enum Priority
    {
        Low = 0,
        Session = 1,
        High = 2
    }

    public class Configuration
    {
        static public readonly string SBConnectionString = "Endpoint=sb://testconversionservicebus.servicebus.windows.net/;SharedAccessKeyName=SendAndListen;SharedAccessKey=Y7Q5ELO+n2l7bsAI9IMaDPBx4X9t+bDOHjnVyXAPdEM=";
        static public readonly int jobsLimit = 4;
        static public readonly int minProcessingMin = 0;
        static public readonly int maxProcessingMin = 0;
        static public readonly int minProcessingSec = 0;
        static public readonly int maxProcessingSec = 2;
        static public readonly int sessionIdle = 30;
    }

    public class CommonUtils
    {
        static public Dictionary<Priority, ConsoleColor> colorDict = new Dictionary<Priority, ConsoleColor>() {
            { Priority.Low, ConsoleColor.Green },
            { Priority.High, ConsoleColor.Yellow },
            { Priority.Session, ConsoleColor.Red } };

        static public Dictionary<Priority, string> queueName = new Dictionary<Priority, string>() {
            { Priority.Low, "lowprio" },
            { Priority.High, "highprio" },
            { Priority.Session, "sessions" } };

    }
}
