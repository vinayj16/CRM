using System;

namespace CRM.Helpers
{
    public static class IndianTime
    {
        private static readonly TimeZoneInfo IST = GetIndianTimeZone();

        private static TimeZoneInfo GetIndianTimeZone()
        {
            // Windows uses "India Standard Time"; Linux containers (Docker/Railway) use "Asia/Kolkata".
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
        }

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IST);

        public static DateTime Today => Now.Date;
    }
}
