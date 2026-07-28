/**
 * Download an array/object as a JSON file in the browser.
 * @param {unknown} data
 * @param {string} filename
 */
export function downloadJsonFile(data, filename) {
  const blob = new Blob([JSON.stringify(data, null, 2)], {
    type: "application/json;charset=utf-8",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/**
 * Fetch details for many ids with limited concurrency.
 * @template T
 * @param {number[]} ids
 * @param {(id: number) => Promise<T>} fetchOne
 * @param {number} [concurrency]
 * @returns {Promise<T[]>}
 */
export async function mapPool(ids, fetchOne, concurrency = 4) {
  const results = [];
  let index = 0;

  async function worker() {
    while (index < ids.length) {
      const i = index++;
      const id = ids[i];
      results[i] = await fetchOne(id);
    }
  }

  const n = Math.min(concurrency, Math.max(1, ids.length));
  await Promise.all(Array.from({ length: n }, () => worker()));
  return results.filter((x) => x != null);
}

export function stampFilename(prefix) {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${prefix}-${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}.json`;
}
