/**
 * Alignment helper lines while dragging a node (Vue Flow helper-lines pattern).
 * Mutates the position change in place when within `distance` px of another node's edge/center.
 */

/**
 * @param {{ id: string, position?: { x: number, y: number }, dragging?: boolean, type: string }} change
 * @param {Array<{ id: string, position: { x: number, y: number }, dimensions?: { width?: number, height?: number } }>} nodes
 * @param {number} [distance=6]
 * @returns {{ horizontal?: number, vertical?: number, snapPosition: { x?: number, y?: number } }}
 */
export function getHelperLines(change, nodes, distance = 6) {
  const defaultResult = {
    horizontal: undefined,
    vertical: undefined,
    snapPosition: { x: undefined, y: undefined },
  };

  const nodeA = nodes.find((node) => node.id === change.id);
  if (!nodeA || !change.position) return defaultResult;

  const width = nodeA.dimensions?.width ?? 0;
  const height = nodeA.dimensions?.height ?? 0;

  const nodeABounds = {
    left: change.position.x,
    right: change.position.x + width,
    top: change.position.y,
    bottom: change.position.y + height,
    width,
    height,
    centerX: change.position.x + width / 2,
    centerY: change.position.y + height / 2,
  };

  let horizontalDistance = distance;
  let verticalDistance = distance;

  return nodes
    .filter((node) => node.id !== nodeA.id)
    .reduce((result, nodeB) => {
      const bw = nodeB.dimensions?.width ?? 0;
      const bh = nodeB.dimensions?.height ?? 0;
      const nodeBBounds = {
        left: nodeB.position.x,
        right: nodeB.position.x + bw,
        top: nodeB.position.y,
        bottom: nodeB.position.y + bh,
        width: bw,
        height: bh,
        centerX: nodeB.position.x + bw / 2,
        centerY: nodeB.position.y + bh / 2,
      };

      const tryVertical = (absDist, snapX, lineX) => {
        if (absDist < verticalDistance) {
          result.snapPosition.x = snapX;
          result.vertical = lineX;
          verticalDistance = absDist;
        }
      };

      const tryHorizontal = (absDist, snapY, lineY) => {
        if (absDist < horizontalDistance) {
          result.snapPosition.y = snapY;
          result.horizontal = lineY;
          horizontalDistance = absDist;
        }
      };

      tryVertical(Math.abs(nodeABounds.left - nodeBBounds.left), nodeBBounds.left, nodeBBounds.left);
      tryVertical(
        Math.abs(nodeABounds.right - nodeBBounds.right),
        nodeBBounds.right - nodeABounds.width,
        nodeBBounds.right,
      );
      tryVertical(Math.abs(nodeABounds.left - nodeBBounds.right), nodeBBounds.right, nodeBBounds.right);
      tryVertical(
        Math.abs(nodeABounds.right - nodeBBounds.left),
        nodeBBounds.left - nodeABounds.width,
        nodeBBounds.left,
      );
      tryVertical(
        Math.abs(nodeABounds.centerX - nodeBBounds.centerX),
        nodeBBounds.centerX - nodeABounds.width / 2,
        nodeBBounds.centerX,
      );

      tryHorizontal(Math.abs(nodeABounds.top - nodeBBounds.top), nodeBBounds.top, nodeBBounds.top);
      tryHorizontal(
        Math.abs(nodeABounds.bottom - nodeBBounds.bottom),
        nodeBBounds.bottom - nodeABounds.height,
        nodeBBounds.bottom,
      );
      tryHorizontal(
        Math.abs(nodeABounds.bottom - nodeBBounds.top),
        nodeBBounds.top - nodeABounds.height,
        nodeBBounds.top,
      );
      tryHorizontal(Math.abs(nodeABounds.top - nodeBBounds.bottom), nodeBBounds.bottom, nodeBBounds.bottom);
      tryHorizontal(
        Math.abs(nodeABounds.centerY - nodeBBounds.centerY),
        nodeBBounds.centerY - nodeABounds.height / 2,
        nodeBBounds.centerY,
      );

      return result;
    }, defaultResult);
}

/**
 * Apply helper-line snap to a Vue Flow nodes-change batch. Returns helper line coords for the overlay.
 * @returns {{ horizontal?: number, vertical?: number }}
 */
export function applyHelperLineChanges(changes, graphNodes, enabled) {
  if (!enabled || !changes?.length) {
    return { horizontal: undefined, vertical: undefined };
  }

  const positionDrags = changes.filter(
    (c) => c.type === "position" && c.dragging && c.position,
  );
  if (positionDrags.length !== 1) {
    return { horizontal: undefined, vertical: undefined };
  }

  const change = positionDrags[0];
  const helperLines = getHelperLines(change, graphNodes);
  if (helperLines.snapPosition.x != null) change.position.x = helperLines.snapPosition.x;
  if (helperLines.snapPosition.y != null) change.position.y = helperLines.snapPosition.y;

  return {
    horizontal: helperLines.horizontal,
    vertical: helperLines.vertical,
  };
}
