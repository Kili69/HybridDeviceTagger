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

/// <summary>Configures Azure RBAC authorization for the management API.</summary>
public sealed class ManagementAuthorizationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ManagementAuthorization";

    /// <summary>Gets or sets the Azure resource ID of the Function App.</summary>
    [Required]
    [RegularExpression(
        "^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Web/sites/[^/]+$",
        ErrorMessage = "FunctionAppResourceId must be a Function App resource ID.")]
    public string FunctionAppResourceId { get; set; } = string.Empty;
}
