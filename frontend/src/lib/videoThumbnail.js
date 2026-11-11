/** Seek just past zero so browsers paint a decoded frame without playing media. */
export function videoThumbnailTime(duration) {
  return Number.isFinite(duration) && duration > 0
    ? Math.min(0.01, duration / 2)
    : 0;
}

/** Owns a thumbnail-only media request; dispose releases it on source changes/unmount. */
export function loadVideoThumbnail(
  video,
  src,
  { onReady, onError, timeoutMs = 15000 },
) {
  let settled = false;
  const timeout = setTimeout(fail, timeoutMs);

  function finish(callback) {
    if (settled) return;
    settled = true;
    clearTimeout(timeout);
    video.pause();
    callback();
  }

  function fail() {
    if (settled) return;
    finish(onError);
    video.removeAttribute("src");
    video.load();
  }

  function seekFrame() {
    if (settled) return;
    const time = videoThumbnailTime(video.duration);
    try {
      if (time > 0) video.currentTime = time;
      else paintFrame();
    } catch {
      fail();
    }
  }

  function paintFrame() {
    if (!video.seeking && video.readyState >= 2 && video.videoWidth > 0)
      finish(onReady);
  }

  video.addEventListener("loadedmetadata", seekFrame);
  video.addEventListener("loadeddata", paintFrame);
  video.addEventListener("seeked", paintFrame);
  video.addEventListener("error", fail);
  video.muted = true;
  video.playsInline = true;
  video.preload = "metadata";
  video.src = src;
  video.load();

  return () => {
    settled = true;
    clearTimeout(timeout);
    video.removeEventListener("loadedmetadata", seekFrame);
    video.removeEventListener("loadeddata", paintFrame);
    video.removeEventListener("seeked", paintFrame);
    video.removeEventListener("error", fail);
    video.pause();
    video.removeAttribute("src");
    video.load();
  };
}
