mergeInto(LibraryManager.library, {
  $AfterechoAudioState: { channel: null },
  AfterechoWeb_MusicState__deps: ['$WEBAudio', '$AfterechoAudioState'],
  AfterechoWeb_MusicState: function(duration, mode) {
    if (!WEBAudio.audioContext || duration<30) return -1000;
    if (mode === 2) return WEBAudio.audioContext.state === 'running' ? 1 : 0;
    function matches(c) {
      var s=c && c.source;
      return s && !s.isPausedMockNode && !s.isStopped && s.buffer &&
        Math.abs(s.buffer.duration-duration)<0.25;
    }
    var channel=AfterechoAudioState.channel;
    if (!matches(channel)) {
      channel=null;
      for (var id in WEBAudio.audioInstances) {
        var candidate=WEBAudio.audioInstances[id];
        if (!matches(candidate)) continue;
        if (channel) return -1002; // Never choose between two active full-length songs.
        channel=candidate;
      }
      AfterechoAudioState.channel=channel;
    }
    if (!channel) return -1000;
    var source=channel.source;
    // Avoid the native stopped-channel frequency fallback (44.1 vs 48 kHz).
    // The actual Web Audio node is authoritative, at the original rate of 1.
    if (channel.pitch !== 1 || source.playbackRate.value !== 1) channel.setPitch(1);
    if (mode === 1) return source.playbackRate.value;
    if (mode === 3) return source.buffer.duration;
    if (mode === 4) return source.buffer.sampleRate;
    return source.estimatePlaybackPosition();
  }
});
