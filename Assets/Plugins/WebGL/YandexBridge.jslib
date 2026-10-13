// Мост между Unity и SDK Яндекс Игр. Сам SDK инициализирует шаблон index.html
// (Assets/WebGLTemplates/Yandex): он кладёт в window обещания ysdkReady и yaPlayerReady.
mergeInto(LibraryManager.library, {

  YaReady: function () {
    if (window.ysdkReady) window.ysdkReady.then(function (ysdk) {
      if (ysdk && ysdk.features && ysdk.features.LoadingAPI) ysdk.features.LoadingAPI.ready();
    });
  },

  YaGameplayStart: function () {
    if (window.ysdkReady) window.ysdkReady.then(function (ysdk) {
      if (ysdk && ysdk.features && ysdk.features.GameplayAPI) ysdk.features.GameplayAPI.start();
    });
  },

  YaGameplayStop: function () {
    if (window.ysdkReady) window.ysdkReady.then(function (ysdk) {
      if (ysdk && ysdk.features && ysdk.features.GameplayAPI) ysdk.features.GameplayAPI.stop();
    });
  },

  YaSave: function (jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    if (window.yaPlayerReady) window.yaPlayerReady.then(function (player) {
      if (player) player.setData({ save: json }, false).catch(function (e) { console.warn('Yandex setData', e); });
    });
  },

  YaRequestLoad: function () {
    var send = function (value) {
      var trySend = function () {
        if (window.unityInstance) window.unityInstance.SendMessage('YandexBridge', 'OnCloudData', value || '');
        else setTimeout(trySend, 100);
      };
      trySend();
    };
    if (!window.yaPlayerReady) { send(''); return; }
    window.yaPlayerReady.then(function (player) {
      if (!player) { send(''); return; }
      player.getData(['save']).then(function (data) { send(data && data.save ? data.save : ''); })
        .catch(function () { send(''); });
    });
  }
});
