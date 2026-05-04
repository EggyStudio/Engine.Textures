namespace Engine;

/// <summary>
/// <see cref="IAssetLoader{T}"/> for <see cref="Texture"/>. Single shared entry point for
/// every backend; dispatches to a concrete <see cref="ITextureDecoder"/> registered with
/// the <see cref="TextureDecoderRegistry"/> based on the file extension.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one loader, not one per backend:</b> <see cref="AssetServer"/> dispatches by
/// extension, so a single loader that fans out to whichever decoder owns the requested
/// extension keeps the AssetServer registration table flat. Backends opt in by calling
/// <see cref="TextureDecoderRegistry.RegisterDecoder"/>; <see cref="TexturesPlugin"/>
/// re-syncs <see cref="Extensions"/> after each backend is brought up so the loader can
/// be re-registered with the AssetServer if the engine adds new texture backends at
/// runtime (rare, but supported for editor scenarios).
/// </para>
/// <para>
/// <b>Color-space hint:</b> the asset server contract has no per-load metadata channel,
/// so the loader honours a sub-asset label of <c>"linear"</c> or <c>"srgb"</c> on
/// <see cref="AssetPath.Label"/>. <c>SceneSpawner</c> attaches the appropriate label when
/// loading textures referenced by a <see cref="SceneMaterialPayload"/>; standalone
/// <c>server.Load&lt;Texture&gt;("foo.png")</c> calls fall back to whatever the decoder
/// decides (usually <see cref="TextureColorSpace.Linear"/>).
/// </para>
/// </remarks>
public sealed class TextureAssetLoader : IAssetLoader<Texture>
{
    private readonly TextureDecoderRegistry _registry;
    private string[] _extensions;

    /// <summary>Creates a loader that dispatches to <paramref name="registry"/>.</summary>
    public TextureAssetLoader(TextureDecoderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _extensions = registry.Extensions.ToArray();
    }

    /// <summary>
    /// Refreshes <see cref="Extensions"/> from the registry. Call after a new decoder
    /// backend has been added so re-registration with the AssetServer picks up the new
    /// extensions.
    /// </summary>
    public void RefreshExtensions() => _extensions = _registry.Extensions.ToArray();

    /// <inheritdoc />
    public string[] Extensions => _extensions;

    /// <inheritdoc />
    public async Task<AssetLoadResult<Texture>> LoadAsync(AssetLoadContext context, CancellationToken ct)
    {
        try
        {
            var ext = context.Path.Extension;
            var decoder = _registry.FindDecoderByExtension(ext);
            if (decoder is null)
                return AssetLoadResult<Texture>.Fail(
                    $"TextureAssetLoader: no ITextureDecoder registered for extension '{ext}' (path: {context.Path}).");

            var settings = ResolveSettings(context.Path.Label);
            var texture = await decoder.DecodeAsync(context, settings, ct);
            return AssetLoadResult<Texture>.Ok(texture);
        }
        catch (Exception ex)
        {
            return AssetLoadResult<Texture>.Fail(
                $"TextureAssetLoader: decode failed for '{context.Path}': {ex.Message}");
        }
    }

    private static TextureLoadSettings ResolveSettings(string? label)
    {
        if (string.IsNullOrEmpty(label)) return TextureLoadSettings.Default;
        return label.ToLowerInvariant() switch
        {
            "srgb"   => new TextureLoadSettings { ColorSpace = TextureColorSpace.Srgb },
            "linear" => new TextureLoadSettings { ColorSpace = TextureColorSpace.Linear },
            _        => TextureLoadSettings.Default,
        };
    }
}