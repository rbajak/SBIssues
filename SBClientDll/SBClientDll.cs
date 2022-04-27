using System;
namespace SBClientDll
{
    public class SBClientDll
    {
        private SBConnector sb = null;

        public SBClientDll()
        {
            sb = new SBConnector();
        }

        public int CallLow(string body)
        {
            return sb.MakeCall(SBCommon.Priority.Low, body).GetAwaiter().GetResult();
        }

        public int CallHigh(string body)
        {
            return sb.MakeCall(SBCommon.Priority.High, body).GetAwaiter().GetResult();
        }

        public int CallSession(string body, string session)
        {
            return sb.MakeCall(SBCommon.Priority.Session, body, session).GetAwaiter().GetResult();
        }
    }
}
