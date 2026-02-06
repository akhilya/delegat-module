<script>
// GoBackButton - A button that navigates back one step in browser history.
import { ArrowLeft } from "lucide-vue-next";
import Button from "./ui/button/Button.vue";

export default {
  components: {
    ArrowLeft,
    Button,
  },
};
</script>

<template>
  <Button variant="ghost" @click="$router.go(-1)">
    <ArrowLeft class="size-4" />
  </Button>
</template>
