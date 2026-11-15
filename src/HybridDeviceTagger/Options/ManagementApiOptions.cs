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

/// <summary>Configures public authentication metadata for the management API.</summary>
public sealed class ManagementApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ManagementApi";

    /// <summary>Gets or sets the Entra application client ID protecting the API.</summary>
    [Required]
    [RegularExpression("^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets the delegated token scope consumed by REST clients.</summary>
    public string TokenScope => $"api://{ClientId}/.default";

    /// <summary>Gets or sets the Microsoft Entra tenant ID used by the configuration portal sign-in.</summary>
    [RegularExpression("^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    public string? TenantId { get; set; }

    /// <summary>Gets or sets the Microsoft Entra sign-in host for the cloud environment.</summary>
    [RegularExpression("^https://[A-Za-z0-9.-]+/?$")]
    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    /// <summary>Gets the delegated scope the configuration portal requests.</summary>
    public string PortalScope => $"api://{ClientId}/access_as_user";
}