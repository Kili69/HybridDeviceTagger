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

using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures age-based Active Directory password rotation.</summary>
public sealed class PasswordRotationOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "PasswordRotation";

    /// <summary>Gets the password age in days at which rotation is due.</summary>
    [Range(1, 29)]
    public int RotateAfterDays { get; init; } = 25;

    /// <summary>Gets the generated password length.</summary>
    [Range(20, 128)]
    public int PasswordLength { get; init; } = 40;

    /// <summary>Gets the lifetime in days assigned to staged Key Vault secrets.</summary>
    [Range(2, 90)]
    public int SecretExpiryDays { get; init; } = 35;

    /// <inheritdoc />
    /// <remarks>
    /// Secret versions must outlive the rotation threshold so the staged credential remains
    /// available throughout the normal recovery window.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SecretExpiryDays <= RotateAfterDays)
        {
            yield return new ValidationResult(
                "SecretExpiryDays must be greater than RotateAfterDays.",
                [nameof(SecretExpiryDays), nameof(RotateAfterDays)]);
        }
    }
}