import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

function apiLocation(conf) {
  const match = conf.match(/location \/api\/ \{[\s\S]*?\n    \}/);
  assert.ok(match, "api location missing");
  return match[0];
}

describe("file download proxy", () => {
  for (const name of ["nginx.conf", "nginx.conf.template"]) {
    it(`${name} keeps a memory window for downloads and does not stall-cut a slow one`, () => {
      const conf = readFileSync(new URL(`../../${name}`, import.meta.url), "utf8");
      const api = apiLocation(conf);
      assert.match(api, /proxy_buffering on;/);
      assert.doesNotMatch(api, /proxy_buffering off;/);
      assert.match(api, /proxy_request_buffering off;/);
      assert.match(api, /proxy_max_temp_file_size 0;/);
      assert.match(api, /proxy_buffer_size 16k;/);
      assert.match(api, /proxy_buffers 16 512k;/);
      assert.match(api, /proxy_busy_buffers_size 1024k;/);
      assert.match(api, /proxy_set_header Connection "";/);
      assert.doesNotMatch(api, /proxy_set_header Connection "upgrade"/);
      assert.match(api, /send_timeout 86400s;/);
      assert.match(api, /proxy_read_timeout 86400s;/);
      assert.match(api, /proxy_send_timeout 86400s;/);
      assert.match(api, /client_body_timeout 86400s;/);
    });
  }
});
