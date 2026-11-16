// Checks that an update really comes from the developer. Every release zip has a signature made with a
// private key that never leaves the developer's PC; the matching public key is built into the app here.
// A copied GitHub account or build server cannot produce that signature, so it cannot ship an update.
//
// Same scheme as MHO Extended Mod Manager: ECDSA, curve P-256, SHA-256, signature as base64 of r||s.
using System;
using System.Security.Cryptography;

namespace MhoCoop
{
    internal static class ReleaseSigning
    {
        // The developer's release key (public half), the same key that signs MHO Extended Mod Manager.
        // Base64 of the standard public-key encoding; its last 64 bytes are the curve point X||Y.
        // Empty would mean a build with no key, which never offers or installs an update.
        private const string BuiltInPublicKey =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVauKwV0PesInDIcwJYxg+5SQnZh7" +
            "xI15tPQi8KvCVklr9HC1CTKDE7ANAtCCBv2cxiqOfOHQkAp2SZsn24bnGA==";

        // Tests only: a key to use instead of the built-in one.
        private const string TestKeyVariable = "MHOCOOP_SIGN_PUBKEY";

        private static string PublicKey
        {
            get
            {
                string test = Environment.GetEnvironmentVariable(TestKeyVariable);
                return string.IsNullOrEmpty(test) ? BuiltInPublicKey : test.Trim();
            }
        }

        public static bool HasKey { get { return PublicKey.Length > 0; } }

        // True when signatureBase64 is this developer's signature of data.
        public static bool Verify(byte[] data, string signatureBase64)
        {
            try
            {
                byte[] key = Convert.FromBase64String(PublicKey);
                byte[] signature = Convert.FromBase64String(signatureBase64.Trim());
                if (key.Length < 64 || signature.Length != 64) return false;

                // Windows wants the key as: "ECS1", the coordinate size, then X and Y.
                byte[] blob = new byte[8 + 64];
                blob[0] = 0x45; blob[1] = 0x43; blob[2] = 0x53; blob[3] = 0x31;
                blob[4] = 32;
                Buffer.BlockCopy(key, key.Length - 64, blob, 8, 64);

                using (CngKey cng = CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob))
                using (ECDsaCng dsa = new ECDsaCng(cng))
                {
                    dsa.HashAlgorithm = CngAlgorithm.Sha256;
                    return dsa.VerifyData(data, signature);
                }
            }
            catch (FormatException) { return false; }
            catch (CryptographicException) { return false; }
        }
    }
}
