using System;
using System.Collections.Generic;
using System.Linq;

namespace LauncherWinUI.Services;

/// <summary>
/// General-variant command buttons use PascalCase CSF keys in CommandButton.ini
/// (e.g. CONTROLBAR:Chem_ConstructGLATunnelNetwork) while slot maps use lowercase bare ids.
/// </summary>
internal static class CsfVariantKeys
{
  public static readonly Dictionary<string, string> CommandBarPascalBare =
      new(StringComparer.OrdinalIgnoreCase)
      {
          ["chem_constructglatunnelnetwork"]     = "Chem_ConstructGLATunnelNetwork",
          ["chem_constructglainfantryrebel"]     = "Chem_ConstructGLAInfantryRebel",
          ["chem_constructglainfantryterrorist"] = "Chem_ConstructGLAInfantryTerrorist",
          ["demo_constructglademotrap"]          = "Demo_ConstructGLADemoTrap",
      };

  public static string NormalizeStorage(string csfId)
  {
      int ci = csfId.IndexOf(':');
      if (ci >= 0)
          return csfId[..ci].ToLowerInvariant() + ":" + csfId[(ci + 1)..].ToLowerInvariant();
      return "controlbar:" + csfId.ToLowerInvariant();
  }

  public static string BareId(string csfId)
  {
      int ci = csfId.IndexOf(':');
      return ci >= 0 ? csfId[(ci + 1)..] : csfId;
  }

  public static bool TryGetCommandBarPascalBare(string bare, out string pascalBare)
      => CommandBarPascalBare.TryGetValue(bare.ToLowerInvariant(), out pascalBare!);

  /// <summary>Preferred CSF storage/display key for a slot label id.</summary>
  public static string ResolveStorageKey(string labelCsfId, IReadOnlyDictionary<string, string>? labels)
  {
      string normalized = NormalizeStorage(labelCsfId);
      string bare = BareId(normalized);

      if (labels is { Count: > 0 })
      {
          if (labels.ContainsKey(labelCsfId))
              return labelCsfId;

          foreach (var key in labels.Keys)
          {
              if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase))
                  return key;
          }

          if (TryGetCommandBarPascalBare(bare, out var pascal))
          {
              foreach (var key in labels.Keys)
              {
                  if (string.Equals(BareId(key), pascal, StringComparison.OrdinalIgnoreCase))
                      return key;
              }
          }
      }

      if (TryGetCommandBarPascalBare(bare, out var canonical))
          return "CONTROLBAR:" + canonical;

      return normalized;
  }

  public static bool TryGetLabelText(
      IReadOnlyDictionary<string, string> labels,
      string labelCsfId,
      out string foundKey,
      out string text)
  {
      foundKey = ResolveStorageKey(labelCsfId, labels);
      if (labels.TryGetValue(foundKey, out text!) && !string.IsNullOrEmpty(text))
          return true;

      foreach (var kv in labels)
      {
          if (!string.Equals(kv.Key, foundKey, StringComparison.OrdinalIgnoreCase)) continue;
          foundKey = kv.Key;
          text = kv.Value;
          return !string.IsNullOrEmpty(text);
      }

      text = "";
      return false;
  }

  /// <summary>Key written into CSF when inserting a new variant label.</summary>
  public static string ToNewCsfKey(string overrideKey)
  {
      string bare = BareId(overrideKey);
      if (TryGetCommandBarPascalBare(bare, out var pascal))
          return "CONTROLBAR:" + pascal;

      int ci = overrideKey.IndexOf(':');
      if (ci >= 0)
      {
          string ns = overrideKey[..ci];
          string seg = overrideKey[(ci + 1)..];
          // Chem_ToolTipGLABuildTunnelNetwork etc. — keep game PascalCase, don't lower-case.
          if (seg.Any(char.IsUpper))
              return ns.ToUpperInvariant() + ":" + seg;
          return ns.ToLowerInvariant() + ":" + seg.ToLowerInvariant();
      }
      return "controlbar:" + overrideKey.ToLowerInvariant();
  }
}
