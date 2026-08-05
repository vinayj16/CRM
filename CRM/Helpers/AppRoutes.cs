namespace CRM.Helpers
{
    public static class AppRoutes
    {
        public static string LeadDetails(int leadId)
        {
            return $"/leaddetails/{IdObfuscator.Encode(leadId)}";
        }

        public static string LeadFollowups(int leadId)
        {
            return $"/leaddetails/{IdObfuscator.Encode(leadId)}#scrollspyFollowups";
        }
    }
}
