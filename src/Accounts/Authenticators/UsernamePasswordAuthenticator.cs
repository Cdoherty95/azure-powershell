﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Hyak.Common;
using Microsoft.Azure.Commands.Common.Authentication;
using Microsoft.Azure.Commands.Common.Authentication.Abstractions;
using Microsoft.Identity.Client;

namespace Microsoft.Azure.PowerShell.Authenticators
{
    /// <summary>
    /// Authenticate username + password scenarios
    /// </summary>
    public class UsernamePasswordAuthenticator : DelegatingAuthenticator
    {
        public override Task<IAccessToken> Authenticate(AuthenticationParameters parameters, CancellationToken cancellationToken)
        {
            var upParameters = parameters as UsernamePasswordParameters;
            var onPremise = upParameters.Environment.OnPremise;
            var authenticationClientFactory = upParameters.AuthenticationClientFactory;
            var resource = upParameters.Environment.GetEndpoint(upParameters.ResourceId) ?? upParameters.ResourceId;
            var scopes = AuthenticationHelpers.GetScope(onPremise, resource);
            var clientId = AuthenticationHelpers.PowerShellClientId;
            var authority = onPremise ?
                                upParameters.Environment.ActiveDirectoryAuthority :
                                AuthenticationHelpers.GetAuthority(parameters.Environment, parameters.TenantId);
            TracingAdapter.Information(string.Format("[UsernamePasswordAuthenticator] Creating IPublicClientApplication - ClientId: '{0}', Authority: '{1}', UseAdfs: '{2}'", clientId, authority, onPremise));
            var publicClient = authenticationClientFactory.CreatePublicClient(clientId: clientId, authority: authority, useAdfs: onPremise);
            TracingAdapter.Information(string.Format("[UsernamePasswordAuthenticator] Calling AcquireTokenByUsernamePassword - Scopes: '{0}', UserId: '{1}'", string.Join(",", scopes), upParameters.UserId));
            var response = publicClient.AcquireTokenByUsernamePassword(scopes, upParameters.UserId, upParameters.Password).ExecuteAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return AuthenticationResultToken.GetAccessTokenAsync(response);
        }

        public override bool CanAuthenticate(AuthenticationParameters parameters)
        {
            return (parameters as UsernamePasswordParameters) != null;
        }
    }
}
