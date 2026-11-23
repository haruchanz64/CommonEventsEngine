using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.Presentation
{
    /// <summary>
    /// Turnkey AudioSource implementation of IEventAudioHandler for SFX and looping BGM.
    /// </summary>
    public sealed class BuiltInEventAudio : MonoBehaviour, IEventAudioHandler
    {
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource bgmSource;

        private void Awake()
        {
            EnsureSources();
        }

        /// <summary>
        /// Lazily attaches dedicated <see cref="AudioSource"/> components for SFX and looping BGM if unassigned.
        /// </summary>
        private void EnsureSources()
        {
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
            }
            if (bgmSource == null)
            {
                bgmSource = gameObject.AddComponent<AudioSource>();
                bgmSource.playOnAwake = false;
                bgmSource.loop = true;
            }
        }

        /// <inheritdoc/>
        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            EnsureSources();
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, volume);
        }

        /// <inheritdoc/>
        public void PlayBgm(AudioClip clip, float volume = 0.8f, bool loop = true)
        {
            if (clip == null) return;
            EnsureSources();
            bgmSource.clip = clip;
            bgmSource.volume = volume;
            bgmSource.loop = loop;
            bgmSource.Play();
        }

        /// <inheritdoc/>
        public void StopBgm()
        {
            if (bgmSource != null && bgmSource.isPlaying)
            {
                bgmSource.Stop();
            }
        }
    }
}
