using System;

namespace CRM.Helpers
{
    public static class IndianTime
    {
        private static readonly TimeZoneInfo IST = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IST);

        public static DateTime Today => Now.Date;
    }
}
