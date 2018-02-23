﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Blazor.Browser.Interop;
using Microsoft.AspNetCore.Blazor.Services;
using System;

namespace Microsoft.AspNetCore.Blazor.Browser.Services
{
    /// <summary>
    /// Default browser implementation of <see cref="IUriHelper"/>.
    /// </summary>
    public class BrowserUriHelper : IUriHelper
    {
        // Since there's only one browser (and hence only one navigation state), the internal state
        // is all static. In typical usage the DI system will register BrowserUriHelper as a singleton
        // so it makes no difference, but if you manually instantiate more than one BrowserUriHelper
        // that's fine too - they will just share their internal state.
        // This class will never be used during server-side prerendering, so we don't have thread-
        // safety concerns due to the static state.
        static readonly string _functionPrefix = typeof(BrowserUriHelper).FullName;
        static bool _hasEnabledNavigationInterception;
        static string _currentAbsoluteUri;
        static EventHandler<string> _onLocationChanged;
        static string _baseUriString;
        static Uri _baseUri;

        /// <inheritdoc />
        public event EventHandler<string> OnLocationChanged
        {
            add
            {
                EnsureNavigationInteceptionEnabled();
                _onLocationChanged += value;
            }
            remove
            {
                // We could consider deactivating the JS-side enableNavigationInteception
                // if there are no remaining listeners, but we don't need that currently.
                _onLocationChanged -= value;
            }
        }

        /// <inheritdoc />
        public string GetBaseUriPrefix()
        {
            EnsureBaseUriPopulated();
            return _baseUriString;
        }

        /// <inheritdoc />
        public string GetAbsoluteUri()
        {
            if (_currentAbsoluteUri == null)
            {
                _currentAbsoluteUri = RegisteredFunction.InvokeUnmarshalled<string>(
                    $"{_functionPrefix}.getLocationHref");
            }

            return _currentAbsoluteUri;
        }

        /// <inheritdoc />
        public Uri ToAbsoluteUri(string relativeUri)
        {
            EnsureBaseUriPopulated();
            return new Uri(_baseUri, relativeUri);
        }

        /// <inheritdoc />
        public string ToBaseRelativePath(string baseUriPrefix, string absoluteUri)
        {
            if (absoluteUri.Equals(baseUriPrefix, StringComparison.Ordinal))
            {
                // Special case: if you're exactly at the base URI, treat it as if you
                // were at "{baseUriPrefix}/" (i.e., with a following slash). It's a bit
                // ambiguous because we don't know whether the server would return the
                // same page whether or not the slash is present, but ASP.NET Core at
                // least does by default when using PathBase.
                return "/";
            }
            else if (absoluteUri.StartsWith(baseUriPrefix, StringComparison.Ordinal)
                && absoluteUri.Length > baseUriPrefix.Length
                && absoluteUri[baseUriPrefix.Length] == '/')
            {
                // The absolute URI must be of the form "{baseUriPrefix}/something",
                // and from that we return "/something"
                return absoluteUri.Substring(baseUriPrefix.Length);
            }

            throw new ArgumentException($"The URI '{absoluteUri}' is not contained by the base URI '{baseUriPrefix}'.");
        }

        private static void EnsureBaseUriPopulated()
        {
            // The <base href> is fixed for the lifetime of the page, so just cache it
            if (_baseUriString == null)
            {
                var baseUri = RegisteredFunction.InvokeUnmarshalled<string>(
                    $"{_functionPrefix}.getBaseURI");
                _baseUriString = ToBaseUriPrefix(baseUri);
                _baseUri = new Uri(_baseUriString);
            }
        }

        private static void NotifyLocationChanged(string newAbsoluteUri)
        {
            _currentAbsoluteUri = newAbsoluteUri;
            _onLocationChanged?.Invoke(null, newAbsoluteUri);
        }

        private static void EnsureNavigationInteceptionEnabled()
        {
            // Don't need thread safety because:
            // (1) there's only one UI thread
            // (2) doesn't matter if we call enableNavigationInteception more than once anyway
            if (!_hasEnabledNavigationInterception)
            {
                _hasEnabledNavigationInterception = true;
                RegisteredFunction.InvokeUnmarshalled<object>(
                    $"{_functionPrefix}.enableNavigationInteception");
            }
        }

        /// <summary>
        /// Given the document's document.baseURI value, returns the URI prefix
        /// that can be prepended to URI paths to produce an absolute URI.
        /// This is computed by removing the final slash and any following characters.
        /// Internal for tests.
        /// </summary>
        /// <param name="baseUri">The page's document.baseURI value.</param>
        /// <returns>The URI prefix</returns>
        internal static string ToBaseUriPrefix(string baseUri)
        {
            if (baseUri != null)
            {
                var lastSlashIndex = baseUri.LastIndexOf('/');
                if (lastSlashIndex >= 0)
                {
                    return baseUri.Substring(0, lastSlashIndex);
                }
            }

            return string.Empty;
        }
    }
}