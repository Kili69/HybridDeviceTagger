/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Security.Cryptography;

namespace HybridDeviceTagger.Services;

/// <summary>Generates cryptographically random passwords without visually ambiguous characters.</summary>
public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    // Ambiguous characters are intentionally omitted to make emergency manual handling safer.
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@_";
    private const string AllCharacters = Uppercase + Lowercase + Digits + Symbols;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is less than four characters.
    /// </exception>
    public string Generate(int length)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var characters = new char[length];
        characters[0] = Pick(Uppercase);
        characters[1] = Pick(Lowercase);
        characters[2] = Pick(Digits);
        characters[3] = Pick(Symbols);

        for (var index = 4; index < characters.Length; index++)
        {
            characters[index] = Pick(AllCharacters);
        }

        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swapIndex]) =
                (characters[swapIndex], characters[index]);
        }

        return new string(characters);
    }

    /// <summary>Selects one uniformly distributed character using a cryptographic RNG.</summary>
    private static char Pick(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];
}