"""Signal-contract tests use synthetic arrays; no SFX generation, keys or network."""
import unittest
import numpy as np
import prepare_folklore_sfx298 as prepare


class PcmContracts(unittest.TestCase):
    def test_overshoot_is_attenuated_before_integer_conversion(self):
        data=np.sin(np.arange(48000)*.12).astype(np.float32)*1.5
        gain=prepare.attenuation(data,1.6)
        raw,decoded=prepare.wav_bytes(data*gain)
        self.assertLess(gain,1)
        self.assertLessEqual(prepare.stats(decoded)['peak'],prepare.PEAK_LIMIT)
        self.assertLessEqual(prepare.stats(decoded)['rms'],prepare.RMS_LIMIT)
        self.assertTrue(raw.startswith(b'RIFF'))
    def test_quiet_signal_is_never_boosted(self):
        data=np.sin(np.arange(48000)*.12).astype(np.float32)*.03
        self.assertEqual(prepare.attenuation(data,.031),1)
    def test_pcm_quantization_is_deterministic(self):
        data=np.sin(np.arange(48000)*.12).astype(np.float32)*.2
        self.assertEqual(prepare.wav_bytes(data)[0],prepare.wav_bytes(data.copy())[0])
    def test_silence_dc_duration_are_not_accepted(self):
        for data,seconds in [(np.zeros(48000,np.float32),1),(np.full(48000,.1,np.float32),1),
                             (np.sin(np.arange(48000)*.12).astype(np.float32)*.1,2)]:
            self.assertFalse(prepare.acceptable(prepare.stats(data),seconds))
    def test_empty_nonfinite_rejected(self):
        for data in [np.array([]),np.array([float('nan')]),np.array([float('inf')])]:
            with self.assertRaises(ValueError):prepare.stats(data)


if __name__=='__main__':unittest.main()
