using FeedCord.Common;

namespace FeedCord.Services.Interfaces
{
    public interface IPostFilterService
    {
        bool ShouldInclude(Post post, string url);
    }
}
