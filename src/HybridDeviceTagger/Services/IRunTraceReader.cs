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

namespace HybridDeviceTagger.Services;

/// <summary>Represents a streamed latest-run trace download.</summary>
/// <param name="Content">The trace content stream, owned and disposed by the HTTP response.</param>
/// <param name="ContentType">The MIME content type of the trace.</param>
/// <param name="FileName">The suggested download file name.</param>
public sealed record RunTraceDownload(
    Stream Content,
    string ContentType,
    string FileName);

/// <summary>Reads the latest device-tagging diagnostic trace.</summary>
public interface IRunTraceReader
{
    /// <summary>Opens the latest trace, or returns <see langword="null"/> before the first run.</summary>
    /// <param name="cancellationToken">Cancels opening the trace.</param>
    /// <returns>The streamed trace, or <see langword="null"/> when none exists.</returns>
    Task<RunTraceDownload?> OpenLatestAsync(CancellationToken cancellationToken);
}