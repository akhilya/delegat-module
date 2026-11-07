/** Decode SSE across arbitrary byte boundaries, including CRLF and multiline data. */
export async function* readSseEvents(response) {
  if (!response.body) throw new Error("No response body");
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  const parse = (frame) => {
    const data = frame
      .split(/\r?\n/)
      .filter((line) => line.startsWith("data:"))
      .map((line) => line.slice(5).replace(/^ /, ""))
      .join("\n");
    try {
      return JSON.parse(data);
    } catch {
      return null;
    }
  };
  try {
    while (true) {
      const { done, value } = await reader.read();
      buffer += done
        ? decoder.decode()
        : decoder.decode(value, { stream: true });
      let boundary;
      while ((boundary = /\r?\n\r?\n/.exec(buffer))) {
        const event = parse(buffer.slice(0, boundary.index));
        buffer = buffer.slice(boundary.index + boundary[0].length);
        if (event) yield event;
      }
      if (done) break;
    }
    // A valid final data record may arrive without its trailing blank line.
    const tail = parse(buffer);
    if (tail) yield tail;
  } finally {
    reader.releaseLock();
  }
}
