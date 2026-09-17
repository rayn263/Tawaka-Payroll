using System.Security.Cryptography;

namespace Tawaka.Application.Security;

/// <summary>
/// Makes a random initial password.
/// <para>
/// Used wherever the application sets a password nobody has chosen — the first administrator at
/// first run, a new user, a reset. It is returned to the caller once, shown once, and only its
/// hash is stored. A well-known default would be in every installation of this software.
/// </para>
/// </summary>
public static class GeneratedPassword
{
    /// <summary>
    /// Sixteen characters from an alphabet with the easily confused ones (O/0, I/l/1) left out,
    /// because this password gets read off a screen and written on paper.
    /// </summary>
    public static string Create()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string all = upper + lower + digits;

        var characters = new List<char>
        {
            Pick(upper),
            Pick(lower),
            Pick(digits)
        };

        while (characters.Count < 16)
        {
            characters.Add(Pick(all));
        }

        // Shuffle so the guaranteed classes are not always in the same positions.
        for (var i = characters.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters.ToArray());

        static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
    }
}
