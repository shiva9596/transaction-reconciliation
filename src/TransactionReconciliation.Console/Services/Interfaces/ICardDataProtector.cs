using System.Security.Cryptography;
using System.Text;
using TransactionReconciliation.Console.Services.Interfaces;

namespace TransactionReconciliation.Console.Services;

public class CardDataProtector : ICardDataProtector
{
    public string ComputeHash(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(cardNumber.Trim());
        var hashBytes = sha256.ComputeHash(bytes);

        return Convert.ToHexString(hashBytes);
    }

    public string GetLast4(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return string.Empty;
        }

        var trimmed = cardNumber.Trim();

        return trimmed.Length <= 4
            ? trimmed
            : trimmed[^4..];
    }
}