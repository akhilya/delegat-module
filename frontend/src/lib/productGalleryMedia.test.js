import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  availableGalleryMedia,
  isOutsideContainedImage,
} from "./productGalleryMedia.js";
import en from "../locales/en.json" with { type: "json" };
import ru from "../locales/ru.json" with { type: "json" };

describe("shared product media editor", () => {
  it("closes on empty contain margins, not on the painted photo", () => {
    const bounds = { left: 50, top: 50, width: 600, height: 600 };
    assert.equal(
      isOutsideContainedImage({ clientX: 350, clientY: 100 }, bounds, 800, 400),
      true,
    );
    assert.equal(
      isOutsideContainedImage({ clientX: 350, clientY: 350 }, bounds, 800, 400),
      false,
    );
    assert.equal(
      isOutsideContainedImage({ clientX: 100, clientY: 350 }, bounds, 400, 800),
      true,
    );
    assert.equal(
      isOutsideContainedImage({ clientX: 350, clientY: 350 }, bounds, 400, 800),
      false,
    );
    assert.equal(
      isOutsideContainedImage({ clientX: 350, clientY: 350 }, bounds, 0, 0),
      true,
    );
  });
  it("includes unattached photos and videos, excludes selected media and documents", () => {
    const files = [
      { id: 1, contentType: "image/jpeg", fileName: "Main.jpg" },
      { id: 2, contentType: "video/mp4", displayName: "Video.mp4" },
      { id: 3, contentType: "image/png", fileName: "Other.png" },
      { id: 4, contentType: "application/pdf", fileName: "Manual.pdf" },
      { id: 5, fileName: "Unknown.mp4" },
    ];
    const available = availableGalleryMedia(files, [{ fileId: "1" }]);
    assert.deepEqual(
      available.map((item) => item.fileId),
      [2, 3],
    );
    assert.equal(available[0].contentType, "video/mp4");
    assert.equal(available[0].displayName, "Video.mp4");
    assert.deepEqual(availableGalleryMedia(), []);
    assert.equal(files.length, 5);
  });

  it("moves detached media back to storage choices without losing its preview metadata", () => {
    const file = {
      id: 2,
      contentType: "video/webm",
      previewUrl: "blob:video",
      fileName: "Clip.webm",
    };
    assert.deepEqual(availableGalleryMedia([file], [{ fileId: 2 }]), []);
    assert.equal(availableGalleryMedia([file], [])[0].previewUrl, "blob:video");
  });

  it("reuses the gallery tiles and parent save flow instead of the legacy image-only endpoint", () => {
    const editor = readFileSync(
      new URL("../components/products/ProductMediaEditor.vue", import.meta.url),
      "utf8",
    );
    const page = readFileSync(
      new URL("../pages/productEditPage.vue", import.meta.url),
      "utf8",
    );
    assert.equal((editor.match(/<ProductGallerySidebar\b/g) || []).length, 2);
    assert.match(editor, /mode="library"/);
    assert.equal((editor.match(/:drag-group="dragGroup"/g) || []).length, 2);
    assert.match(editor, /const available = ref\(\[\]\)/);
    assert.doesNotMatch(editor, /v-if="available.length"/);
    assert.doesNotMatch(
      editor,
      /postProductImages|useDebounceFn|imageFile\.vue/,
    );
    assert.doesNotMatch(page, /ProductImagesEditor/);
    const section = page.match(/<ProductMediaEditor[\s\S]*?\/>/)[0];
    for (const binding of [
      'v-model:images="sortedImages"',
      '@drag-change="onGalleryDragChange"',
      '@remove="removeGalleryImage"',
      '@add="addGalleryMedia"',
      '@upload="triggerGalleryUpload"',
      '@preview="openLightboxByFileId"',
    ])
      assert.ok(section.includes(binding), binding);
    assert.match(page, /\['properties', 'images'\]\.includes\(tab\)/);
    assert.match(page, /<TabsTrigger value="images">/);
  });

  it("localizes Media and storage actions in both locales", () => {
    for (const messages of [en, ru]) {
      assert.equal(typeof messages.pages.productEditPage.tabs.media, "string");
      assert.equal(
        typeof messages.products.productGallerySidebar.addToGallery,
        "string",
      );
      assert.equal(
        typeof messages.products.productGallerySidebar.dropToRemove,
        "string",
      );
      for (const key of ["galleryTitle", "availableTitle", "availableHint"])
        assert.equal(
          typeof messages.components.productMediaEditor[key],
          "string",
        );
    }
  });

  it("uses larger responsive media tiles, filled photo crops, and a viewport-sized full preview", () => {
    const editor = readFileSync(
      new URL("../components/products/ProductMediaEditor.vue", import.meta.url),
      "utf8",
    );
    const gallery = readFileSync(
      new URL(
        "../components/products/ProductGallerySidebar.vue",
        import.meta.url,
      ),
      "utf8",
    );
    const page = readFileSync(
      new URL("../pages/productEditPage.vue", import.meta.url),
      "utf8",
    );
    assert.equal((editor.match(/layout="grid"/g) || []).length, 2);
    assert.match(gallery, /minmax\(min\(100%,12rem\),1fr\)/);
    assert.match(gallery, /img-class="[^"]*size-full object-cover"/);
    assert.doesNotMatch(gallery, /blur-backdrop/);
    const lightbox = page.slice(page.indexOf("<!-- Lightbox overlay -->"));
    assert.match(lightbox, /img-class="size-full object-contain"/);
    assert.equal(
      (lightbox.match(/h-\[calc\(100dvh-8rem\)\]/g) || []).length,
      2,
    );
    assert.doesNotMatch(lightbox, /max-h-\[90vh\]/);
    assert.match(page, /<AlertDialog v-if="tab === 'properties'">/);
    assert.match(page, /form="product-edit-form"/);
    assert.match(lightbox, /@click="onLightboxImageClick"/);
    assert.doesNotMatch(
      lightbox,
      /bg-background\/90|backdrop-filter:backdrop-blur/,
    );
    assert.doesNotMatch(gallery, /bg-black\/60/);
    assert.match(gallery, /v-if="images\?\.length \|\| dragGroup"/);
    assert.match(gallery, /:sort="mode === 'gallery'"/);
  });
});
