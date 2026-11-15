/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 */

using System.Security.Cryptography;

namespace HybridDeviceTagger.Services;

/// <summary>Generates cryptographically random passwords without visually ambiguous characters.</summary>
public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    // Ambiguous characters are intentionally omitted to make emergency manual handling safer.

    /// <summary>Upper-case letters without <c>I</c> and <c>O</c>.</summary>
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Lower-case letters without <c>l</c>.</summary>
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";

    /// <summary>Digits without <c>0</c> and <c>1</c>.</summary>
    private const string Digits = "23456789";

    /// <summary>Special characters that count toward the Active Directory complexity requirement.</summary>
    private const string Symbols = "!#$%&*+-=?@_";

    /// <summary>The combined alphabet used for all positions after the four guaranteed classes.</summary>
    private const string AllCharacters = Uppercase + Lowercase + Digits + Symbols;

    /// <inheritdoc />
    /// <remarks>
    /// One character from each class is placed first to satisfy AD complexity rules, then a
    /// Fisher-Yates shuffle with a cryptographic RNG removes the predictable class positions.
    /// </remarks>
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
    /// <param name="characters">The alphabet to choose from.</param>
    /// <returns>The selected character.</returns>
    private static char Pick(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];
}