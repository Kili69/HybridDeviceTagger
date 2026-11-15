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

/// <summary>Configures where the activity log is queried from.</summary>
public sealed class MonitoringOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Gets or sets the resource ID of the workspace-based Application Insights component that
    /// receives the activity log. When empty, the activity log is written but cannot be queried.
    /// </summary>
    [RegularExpression(
        "(?i)^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Insights/components/[^/]+$",
        ErrorMessage = "ApplicationInsightsResourceId must be an Application Insights resource ID.")]
    public string? ApplicationInsightsResourceId { get; set; }
}
