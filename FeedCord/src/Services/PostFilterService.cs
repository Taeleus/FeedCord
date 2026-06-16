using FeedCord.Common;
using FeedCord.Services.Helpers;
using FeedCord.Services.Interfaces;

namespace FeedCord.Services
{
    public class PostFilterService : IPostFilterService
    {
        private readonly Config _config;

        public PostFilterService(Config config)
        {
            _config = config;
        }

        public bool ShouldInclude(Post post, string url)
        {
            if (_config.PostFilters == null || !_config.PostFilters.Any())
            {
                return true;
            }

            var exactFilter = _config.PostFilters.FirstOrDefault(wf => string.Equals(wf.Url, url, StringComparison.OrdinalIgnoreCase));
            if (exactFilter != null)
            {
                return FilterConfigs.GetFilterSuccess(post, exactFilter.Filters.ToArray());
            }

            var allFilter = _config.PostFilters.FirstOrDefault(wf => string.Equals(wf.Url, "all", StringComparison.OrdinalIgnoreCase));
            if (allFilter != null)
            {
                return FilterConfigs.GetFilterSuccess(post, allFilter.Filters.ToArray());
            }

            return true;
        }
    }
}
