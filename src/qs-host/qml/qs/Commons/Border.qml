pragma Singleton
import QtQuick

// Omarchy's border helpers (qs.Commons Border), flat: a border is its colour and a width.
QtObject {
  function localOrSurfaceSpec(section, key, color, fallback, width) { return { color: color, width: width } }
  function top(spec) { return spec ? spec.width : 0 }
  function bottom(spec) { return spec ? spec.width : 0 }
}
