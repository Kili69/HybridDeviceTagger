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

namespace HybridDeviceTagger.Models;

/// <summary>Represents an Active Directory computer that has a hybrid join device identifier.</summary>
/// <param name="DeviceId">The AD <c>objectGUID</c> mapped to the Entra <c>deviceId</c>.</param>
/// <param name="Name">The computer name.</param>
/// <param name="DistinguishedName">The computer object's distinguished name.</param>
public sealed record DirectoryDevice(Guid DeviceId, string Name, string DistinguishedName);