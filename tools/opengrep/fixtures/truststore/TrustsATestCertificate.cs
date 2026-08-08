// Fixture for no-certificate-store-access. This file is in no project and is
// never compiled; it exists so the rule can be watched refusing the mistake it
// names.
//
// The near-miss is the one that arrives with the first test against a real
// HTTPS endpoint: the endpoint is a self-signed one the test starts itself, the
// handshake fails, and the shortest way past that is to put the certificate
// where the machine will believe it. It works, the test goes green, and every
// other program on that machine trusts the certificate afterwards, including
// long after the run that installed it.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class TrustsATestCertificate
{
    // Legal neighbour, left here on purpose: reading a certificate out of bytes
    // the test brought with it touches no store, and the rule has to stay quiet
    // on it.
    public static X509Certificate2 LoadFromBytes(byte[] pkcs12)
    {
        return X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
    }

    // The regression.
    public static void Trust(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }
}
