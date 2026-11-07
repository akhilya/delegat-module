<template>
  <!-- SettingsShell owns the page chrome; nested pages retain their filter state. -->
  <Teleport defer to="#settings-navbar-tools">
    <slot />
  </Teleport>
</template>
