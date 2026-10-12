// Map units of the replay: roughly −8200..8200 on both axes, Radiant's base bottom-left. The maps' SVG viewBox
// uses the same units, with y flipped (SVG y grows downwards; the map's grows upwards).

export const EXTENT = 8200;
// A margin around the map, so marks placed right at its edge are not cut.
const PADDING = 500;
export const VIEW_BOX = `${-EXTENT - PADDING} ${-EXTENT - PADDING} ${2 * (EXTENT + PADDING)} ${2 * (EXTENT + PADDING)}`;

export const svgY = y => -y;
