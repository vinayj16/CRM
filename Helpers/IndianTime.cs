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

        /// <summary>
        /// Converts a UTC-stored DateTime (as persisted by the MongoDB driver) back
        /// to Indian Standard Time. DateTimes stored via IndianTime.Now are converted
        /// to UTC by the Mongo driver, so on read-back they must be converted again
        /// before comparing with IndianTime.Today / Now.
        /// </summary>
        public static DateTime ToIst(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
                return TimeZoneInfo.ConvertTimeFromUtc(dt, IST);
            if (dt.Kind == DateTimeKind.Local)
                return TimeZoneInfo.ConvertTime(dt, TimeZoneInfo.Local, IST);
            return dt; // Unspecified — assumed to already be IST
        }
    }
}
