import { createStore as _createStore } from "vuex";

import authModule from "./modules/authModule";
import userModule from "./modules/userModule";
import productModule from "./modules/productModule";
import categoryModule from "./modules/categoryModule";
import productGroupModule from "./modules/productGroupModule";
import fileModule from "./modules/fileModule";
import imageModule from "./modules/imageModule";
import noteModule from "./modules/noteModule";
import storeModule from "./modules/storeModule";
import storeAttributeModule from "./modules/storeAttributeModule";
import tokenModule from "./modules/tokenModule";
import marketplaceModule from "./modules/marketplaceModule";
import importProgress from "./modules/importProgressModule";

/**
 * Creates and configures the Vuex store with all registered modules.
 * @returns {import('vuex').Store} The configured Vuex store instance.
 */
export function createStore() {
  return _createStore({
    modules: {
      authModule,
      userModule,
      productModule,
      categoryModule,
      productGroupModule,
      fileModule,
      imageModule,
      noteModule,
      storeModule,
      storeAttributeModule,
      tokenModule,
      marketplaceModule,
      importProgress,
    },
  });
}
