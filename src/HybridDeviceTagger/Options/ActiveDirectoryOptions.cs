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

/// <summary>Configures secure LDAP access to Active Directory.</summary>
public sealed class ActiveDirectoryOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ActiveDirectory";

    /// <summary>
    /// Gets the initial Active Directory DNS domain used for domain-controller discovery. Empty until
    /// the forest is configured after installation through the portal or management module.
    /// </summary>
    [StringLength(253)]
    public string Domain { get; init; } = string.Empty;

    /// <summary>Gets an optional domain-controller DNS name that bypasses DNS SRV discovery.</summary>
    [StringLength(253)]
    public string? Server { get; init; }

    /// <summary>Gets the LDAPS port tried before StartTLS.</summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 636;

    /// <summary>Gets a value indicating whether TLS is enabled for LDAP.</summary>
    public bool UseSsl { get; init; } = true;

    /// <summary>Gets a value indicating whether LDAPS may fall back to LDAP with StartTLS.</summary>
    public bool AllowLdapFallback { get; init; } = true;

    /// <summary>Gets the port used for LDAP with StartTLS.</summary>
    [Range(1, 65535)]
    public int LdapFallbackPort { get; init; } = 389;

    /// <summary>
    /// Gets the initial user name used for LDAP binds. Empty until the service account is configured
    /// after installation through the portal or management module.
    /// </summary>
    [StringLength(256)]
    public string BindUsername { get; init; } = string.Empty;

    /// <summary>Gets the optional service-account DN; when absent, the Function resolves it from the UPN.</summary>
    [StringLength(2048)]
    public string BindUserDistinguishedName { get; init; } = string.Empty;

    /// <summary>Gets the LDAP server-side page size.</summary>
    [Range(1, 10000)]
    public int PageSize { get; init; } = 500;

    /// <summary>Gets an optional PEM or DER certificate used as a private LDAP trust anchor.</summary>
    [StringLength(4096)]
    public string? TrustedCertificatePath { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Both LDAPS and StartTLS protect credentials and password operations with TLS.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseSsl)
        {
            yield return new ValidationResult(
                "UseSsl must be enabled so LDAP credential and password operations use LDAPS or StartTLS.",
                [nameof(UseSsl)]);
        }

        if (AllowLdapFallback && LdapFallbackPort == Port)
        {
            yield return new ValidationResult(
                "LdapFallbackPort must differ from the LDAPS port.",
                [nameof(LdapFallbackPort), nameof(Port)]);
        }
    }
}