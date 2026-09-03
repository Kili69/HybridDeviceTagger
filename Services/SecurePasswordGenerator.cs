using System.Security.Cryptography;

namespace HybridDeviceTagger.Services;

public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@_";
    private const string AllCharacters = Uppercase + Lowercase + Digits + Symbols;

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

    private static char Pick(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];
}