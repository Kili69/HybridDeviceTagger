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

using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates that tagging rules can only target domains in the configured AD forest.</summary>
public interface IActiveDirectoryRuleValidator
{
    /// <summary>Validates every domain-specific rule against domains discovered from the forest.</summary>
    /// <param name="rules">The complete normalized tagging-rule collection.</param>
    /// <param name="cancellationToken">Cancels forest discovery.</param>
    /// <exception cref="TaggingConfigurationValidationException">
    /// A rule contains a domain that is not advertised by the configured Active Directory forest.
    /// </exception>
    Task ValidateRulesAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        CancellationToken cancellationToken);
}
