import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  isSyntheticMarketplaceCategoryLabel,
  mappingDisplayName,
} from "./mappingDisplayName.js";

describe("isSyntheticMarketplaceCategoryLabel", () => {
  it("treats Category {id} and the raw id as placeholders", () => {
    assert.equal(
      isSyntheticMarketplaceCategoryLabel(
        "Category 17028973:115944384",
        "17028973:115944384",
      ),
      true,
    );
    assert.equal(
      isSyntheticMarketplaceCategoryLabel(
        "17028973:115944384",
        "17028973:115944384",
      ),
      true,
    );
  });

  it("keeps a marketplace path", () => {
    assert.equal(
      isSyntheticMarketplaceCategoryLabel(
        "Apparel / T-shirts",
        "17028973:115944384",
      ),
      false,
    );
  });
});

describe("mappingDisplayName", () => {
  it("skips the placeholder and uses the local category", () => {
    assert.equal(
      mappingDisplayName(
        {
          name: null,
          externalCategoryName: "Category 17028973:115944384",
          externalCategoryId: "17028973:115944384",
          localCategoryName: "T-shirts",
          id: 9,
        },
        "Mapping #9",
      ),
      "T-shirts",
    );
  });

  it("prefers a real mapping name", () => {
    assert.equal(
      mappingDisplayName({
        name: "Ozon summer",
        externalCategoryName: "Apparel / T-shirts",
        localCategoryName: "T-shirts",
      }),
      "Ozon summer",
    );
  });
});
