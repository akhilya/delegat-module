<template>
  <Page>
    <NavigationBar />
    <Breadcrumbs :items="[{ label: 'Настройки', to: '/settings' }, { label: 'AI Модели' }]" />

    <PageSection>
      <template #header>
        <div style="display:flex; align-items:center; justify-content:space-between;">
          <span>AI Модели</span>
          <Button @click="openCreate">Добавить модель</Button>
        </div>
      </template>

      <div v-if="isLoading" class="ai-models__loading">Загрузка...</div>

      <div v-else-if="models.length === 0" class="ai-models__empty">
        Нет настроенных AI-моделей. Добавьте модель для включения AI-ассистента.
      </div>

      <table v-else class="ai-models__table">
        <thead>
          <tr>
            <th>Название</th>
            <th>Провайдер</th>
            <th>Model ID</th>
            <th>API URL</th>
            <th>По умолчанию</th>
            <th>Действия</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="model in models" :key="model.id">
            <td>{{ model.name }}</td>
            <td>{{ model.provider === 0 ? "Anthropic" : "OpenAI-compatible" }}</td>
            <td><code>{{ model.modelId }}</code></td>
            <td class="ai-models__url">{{ model.apiUrl }}</td>
            <td>
              <span v-if="model.isDefault" class="ai-models__badge">✓ Основная</span>
              <button v-else class="ai-models__link" @click="setDefault(model.id)">Сделать основной</button>
            </td>
            <td class="ai-models__actions">
              <button class="ai-models__action" @click="testModel(model.id)" :disabled="testingId === model.id">
                {{ testingId === model.id ? "..." : "Проверить" }}
              </button>
              <button class="ai-models__action" @click="openEdit(model)">Изменить</button>
              <button class="ai-models__action ai-models__action--danger" @click="deleteModel(model.id)">Удалить</button>
            </td>
          </tr>
        </tbody>
      </table>
    </PageSection>

    <!-- Modal -->
    <div v-if="showModal" class="ai-modal-overlay" @click.self="closeModal">
      <div class="ai-modal">
        <div class="ai-modal__header">
          <span>{{ editingModel ? "Изменить модель" : "Добавить модель" }}</span>
          <button class="ai-modal__close" @click="closeModal">✕</button>
        </div>

        <div class="ai-modal__body">
          <div class="ai-modal__field">
            <label>Название</label>
            <input v-model="form.name" placeholder="Claude Sonnet" />
          </div>
          <div class="ai-modal__field">
            <label>Провайдер</label>
            <select v-model="form.provider">
              <option :value="0">Anthropic</option>
              <option :value="1">OpenAI-compatible</option>
            </select>
          </div>
          <div class="ai-modal__field">
            <label>API URL</label>
            <input v-model="form.apiUrl" placeholder="https://api.anthropic.com/v1/messages" />
          </div>
          <div class="ai-modal__field">
            <label>API ключ {{ editingModel ? "(оставьте пустым чтобы не менять)" : "" }}</label>
            <input v-model="form.apiKey" type="password" placeholder="sk-..." />
          </div>
          <div class="ai-modal__field">
            <label>Model ID</label>
            <input v-model="form.modelId" placeholder="claude-sonnet-4-6" />
          </div>
          <div class="ai-modal__field">
            <label>Max Tokens</label>
            <input v-model.number="form.maxTokens" type="number" placeholder="8192" />
          </div>
        </div>

        <div class="ai-modal__footer">
          <Button variant="ghost" @click="closeModal">Отмена</Button>
          <Button :disabled="isSaving" @click="save">{{ isSaving ? "Сохранение..." : "Сохранить" }}</Button>
        </div>
      </div>
    </div>
  </Page>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { useStore } from "vuex";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";

const store = useStore();
const models = ref([]);
const isLoading = ref(false);
const showModal = ref(false);
const editingModel = ref(null);
const isSaving = ref(false);
const testingId = ref(null);

const defaultForm = () => ({
  name: "",
  apiUrl: "https://api.anthropic.com/v1/messages",
  apiKey: "",
  modelId: "claude-sonnet-4-6",
  provider: 0,
  maxTokens: 8192,
});

const form = ref(defaultForm());

async function load() {
  isLoading.value = true;
  store.dispatch("aiModels/getAiModels", {
    onSuccess: (r) => { models.value = r.data; },
    onError: displaySonnerError,
  }).finally(() => { isLoading.value = false; });
}

function openCreate() {
  editingModel.value = null;
  form.value = defaultForm();
  showModal.value = true;
}

function openEdit(model) {
  editingModel.value = model;
  form.value = {
    name: model.name,
    apiUrl: model.apiUrl,
    apiKey: "",
    modelId: model.modelId,
    provider: model.provider,
    maxTokens: model.maxTokens,
  };
  showModal.value = true;
}

function closeModal() {
  showModal.value = false;
  editingModel.value = null;
}

async function save() {
  isSaving.value = true;
  const data = { ...form.value };
  if (editingModel.value && !data.apiKey) delete data.apiKey;

  const action = editingModel.value
    ? store.dispatch("aiModels/updateAiModel", { id: editingModel.value.id, data, onSuccess: () => {}, onError: () => {} })
    : store.dispatch("aiModels/createAiModel", { data, onSuccess: () => {}, onError: () => {} });

  try {
    if (editingModel.value) {
      await store.dispatch("aiModels/updateAiModel", {
        id: editingModel.value.id, data,
        onSuccess: () => {}, onError: (e) => { throw e; },
      });
    } else {
      await store.dispatch("aiModels/createAiModel", {
        data,
        onSuccess: () => {}, onError: (e) => { throw e; },
      });
    }
    displaySonnerSuccess("Модель сохранена");
    closeModal();
    load();
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isSaving.value = false;
  }
}

async function deleteModel(id) {
  if (!confirm("Удалить AI-модель?")) return;
  await store.dispatch("aiModels/deleteAiModel", {
    id,
    onSuccess: () => { displaySonnerSuccess("Модель удалена"); load(); },
    onError: displaySonnerError,
  });
}

async function setDefault(id) {
  await store.dispatch("aiModels/setDefaultAiModel", {
    id,
    onSuccess: () => { displaySonnerSuccess("Основная модель изменена"); load(); },
    onError: displaySonnerError,
  });
}

async function testModel(id) {
  testingId.value = id;
  await store.dispatch("aiModels/testAiModel", {
    id,
    onSuccess: (r) => {
      if (r.data.success) displaySonnerSuccess(r.data.message);
      else displaySonnerError(new Error(r.data.message));
    },
    onError: displaySonnerError,
  });
  testingId.value = null;
}

onMounted(load);
</script>

<style scoped>
.ai-models__loading, .ai-models__empty {
  padding: 24px;
  color: var(--color-text-secondary, #64748b);
  text-align: center;
}

.ai-models__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
.ai-models__table th, .ai-models__table td {
  padding: 10px 12px;
  text-align: left;
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}
.ai-models__table th { font-weight: 600; color: var(--color-text-secondary, #64748b); }
.ai-models__url { max-width: 180px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.ai-models__badge {
  background: #dcfce7; color: #16a34a;
  border-radius: 4px; padding: 2px 8px; font-size: 12px;
}
.ai-models__link {
  background: none; border: none; color: var(--color-primary, #5b6ee1);
  cursor: pointer; font-size: 12px; text-decoration: underline;
}

.ai-models__actions { display: flex; gap: 6px; }
.ai-models__action {
  padding: 4px 10px; border-radius: 6px; border: 1px solid var(--color-border, #e2e8f0);
  background: transparent; cursor: pointer; font-size: 12px; color: var(--color-text, #1e293b);
}
.ai-models__action:hover { background: var(--color-hover, #f1f5f9); }
.ai-models__action:disabled { opacity: 0.5; cursor: not-allowed; }
.ai-models__action--danger { color: #dc2626; border-color: #fecaca; }
.ai-models__action--danger:hover { background: #fef2f2; }

.ai-modal-overlay {
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.4);
  z-index: 2000;
  display: flex; align-items: center; justify-content: center;
}
.ai-modal {
  background: var(--color-surface, #fff);
  border-radius: 12px;
  width: 440px;
  max-width: 90vw;
  box-shadow: 0 16px 48px rgba(0,0,0,0.2);
}
.ai-modal__header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  font-weight: 600;
}
.ai-modal__close {
  border: none; background: none; cursor: pointer; font-size: 16px;
  color: var(--color-text-secondary, #64748b);
}
.ai-modal__body {
  padding: 20px;
  display: flex; flex-direction: column; gap: 14px;
}
.ai-modal__field { display: flex; flex-direction: column; gap: 4px; }
.ai-modal__field label { font-size: 12px; font-weight: 500; color: var(--color-text-secondary, #64748b); }
.ai-modal__field input, .ai-modal__field select {
  padding: 8px 10px;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  font-size: 13px;
  font-family: inherit;
  background: var(--color-surface, #fff);
  outline: none;
}
.ai-modal__field input:focus, .ai-modal__field select:focus {
  border-color: var(--color-primary, #5b6ee1);
}
.ai-modal__footer {
  display: flex; justify-content: flex-end; gap: 8px;
  padding: 14px 20px;
  border-top: 1px solid var(--color-border, #e2e8f0);
}
</style>
