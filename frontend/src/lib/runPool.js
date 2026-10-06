/**
 * Runs async work over a list with a fixed number of workers.
 * Results stay in input order. A rejection stops scheduling further items.
 */
export async function mapPool(items, worker, concurrency = 3) {
  const list = Array.from(items);
  const results = new Array(list.length);
  let next = 0;

  let failed = false;

  async function run() {
    while (!failed && next < list.length) {
      const index = next;
      next += 1;
      try {
        results[index] = await worker(list[index], index);
      } catch (error) {
        failed = true;
        throw error;
      }
    }
  }

  const workers = Math.min(Math.max(1, concurrency), list.length);
  if (workers === 0) return results;
  await Promise.all(Array.from({ length: workers }, () => run()));
  return results;
}
