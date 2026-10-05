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

/// <summary>Configures the Key Vault used for Active Directory credentials.</summary>
public sealed class KeyVaultOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "KeyVault";

    /// <summary>Gets the Key Vault service URI.</summary>
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    [Url]
    public string VaultUri { get; init; } = string.Empty;

    /// <summary>Gets the secret name containing the active password.</summary>
    [Required]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[0-9A-Za-z-]+$")]
    public string CurrentPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-current";

    /// <summary>Gets the secret name containing the staged recovery password.</summary>
    [Required]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[0-9A-Za-z-]+$")]
    public string NextPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-next";
}