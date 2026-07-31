using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeMeridian.Evolution.Domain.Ledger;

public static class JournalHash
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(
        long sequence,
        DateTimeOffset appendedAt,
        string previousHash,
        CognitiveTransaction transaction)
    {
        var transactionJson = JsonSerializer.Serialize(transaction, JsonOptions);
        var canonical = FormattableString.Invariant(
            $"{sequence}|{appendedAt:O}|{previousHash}|{transactionJson}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
