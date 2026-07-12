using System.Text;

namespace Binexus.Modules.Identity.Application;

public static class EmailNormalizer
{
    public static string Normalize(string email) =>
        email.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
