namespace DotaAnalyst.Domain.Players;

// Public Steam profile of a player: the name shown in Steam and the avatar picture.
public record PlayerProfile(string PersonaName, Uri? AvatarUrl);
