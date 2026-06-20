using System.Security.Cryptography;
using System.Text;
using FeedCord.Common;

namespace FeedCord.Services.Helpers;

public static class PostIdentity
{
    public static string GetStableId(Post post)
    {
        if (!string.IsNullOrWhiteSpace(post.ItemId))
            return post.ItemId.Trim();

        if (!string.IsNullOrWhiteSpace(post.Link))
            return post.Link.Trim();

        var source = string.Join(
            "\n",
            post.Title,
            post.Author,
            post.PublishDate.ToUniversalTime().ToString("O"),
            post.Description);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash);
    }
}
