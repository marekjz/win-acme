﻿using PKISharp.WACS.Clients.IIS;
using PKISharp.WACS.DomainObjects;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services;
using System.Linq;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.TargetPlugins
{
    internal class IIS : ITargetPlugin
    {
        private readonly ILogService _log;
        private readonly IISOptions _options;
        private readonly IISHelper _helper;
        private readonly UserRoleService _userRoleService;

        public IIS(
            ILogService logService, UserRoleService roleService,
            IISHelper helper, IISOptions options)
        {
            _log = logService;
            _options = options;
            _helper = helper;
            _userRoleService = roleService;
        }

        public async Task<Target> Generate()
        {
            // Check if we have any bindings
            var allBindings = _helper.GetBindings();
            var filteredBindings = _helper.FilterBindings(allBindings, _options);
            if (filteredBindings.Count() == 0)
            {
                _log.Error("No bindings matched, unable to proceed");
                return new NullTarget();
            }

            // Generate friendly name suggestion
            var friendlyNameSuggestion = "[IIS]";
            if (_options.IncludeSiteIds != null && _options.IncludeSiteIds.Any())
            {
                var filterSites = _helper.GetSites(false).Where(x => _options.IncludeSiteIds.Contains(x.Id));
                var unmatchedIds = _options.IncludeSiteIds.Where(x => !filterSites.Any(s => s.Id == x));
                var labels = filterSites.Select(s => s.Name).ToList();
                labels.AddRange(unmatchedIds.Select(s => $"#{s}"));
                var sites = string.Join(", ", labels);
                friendlyNameSuggestion += $" {sites}";
            } 
            else
            {
                friendlyNameSuggestion += $" (any site)";
            }

            if (!string.IsNullOrEmpty(_options.IncludePattern))
            {
                friendlyNameSuggestion += $" | {_options.IncludePattern}";
            }
            else if (_options.IncludeHosts != null && _options.IncludeHosts.Any())
            {
                var hosts = string.Join(',', _options.IncludeHosts);
                friendlyNameSuggestion += $" | {hosts}";
            }
            else if (_options.IncludeRegex != null)
            {
                friendlyNameSuggestion += $" | {_options.IncludeRegex}";
            }
            else
            {
                friendlyNameSuggestion += $" | (any host)";
            }

            // Handle common name
            var cn = _options.CommonName ?? "";
            var cnDefined = !string.IsNullOrWhiteSpace(cn);
            var cnValid = cnDefined && filteredBindings.Any(x => x.HostUnicode == cn);
            if (cnDefined && !cnValid)
            {
                _log.Warning("Specified common name {cn} not valid", cn);
            }

            // Return result
            var commonName = cnValid ? cn : filteredBindings.First().HostUnicode;
            var parts = filteredBindings.
                GroupBy(x => x.SiteId).
                Select(group => new TargetPart(group.Select(x => x.HostUnicode))
                {
                    SiteId = group.Key
                });
            return new Target(friendlyNameSuggestion, commonName, parts);
        }

        bool IPlugin.Disabled => Disabled(_userRoleService);
        internal static bool Disabled(UserRoleService userRoleService) => !userRoleService.AllowIIS;
    }
}
