using System.Security.Cryptography;
using System.Text;

namespace CRM.Helpers
{
    public static class IdObfuscator
    {
        private const string SecretKey = "CRM-S3cur3-K3y-2024!";

        public static string Encode(int id)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{SecretKey}"));
            var sig = Convert.ToHexString(hash)[..10].ToLower();
            return $"{id:x}{sig}"; // hex id + signature, no dash
        }

        public static int? Decode(string token)
        {
            try
            {
                if (token.Length <= 10) return null;
                var idHex = token[..^10];
                var id = Convert.ToInt32(idHex, 16);
                return Encode(id) == token.ToLower() ? id : null;
            }
            catch { return null; }
        }
        private static Dictionary<string, string> Routes = new()
        {
            { "Lead", "/leaddetails" },
            { "property", "/propertiesdetails" },
            { "Agent", "/Agentdetails" },
            { "Booking", "/Bookingdetails" },
        };

        public static string Url(string entity, int id)
        {
            var prefix = Routes.ContainsKey(entity) ? Routes[entity] : $"/{entity.ToLower()}details";
            return $"{prefix}/{Encode(id)}";
        }
    }
}