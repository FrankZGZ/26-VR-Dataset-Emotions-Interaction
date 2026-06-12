import argparse
import copy
import glob
import json
import os
import sys
import time
import traceback
from pathlib import Path


protocol_out = sys.stdout
sys.stdout = sys.stderr


def protocol(payload: dict) -> None:
    protocol_out.write(json.dumps(payload, ensure_ascii=False) + "\n")
    protocol_out.flush()


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model_path", default="microsoft/VibeVoice-Realtime-0.5B")
    parser.add_argument("--speaker_name", default="Emma")
    parser.add_argument("--output_dir", required=True)
    parser.add_argument("--device", default="")
    parser.add_argument("--cfg_scale", type=float, default=1.5)
    return parser.parse_args()


class VoiceMapper:
    def __init__(self):
        voices_dir = Path(__file__).resolve().parents[2] / "VibeVoice" / "demo" / "voices" / "streaming_model"
        if not voices_dir.exists():
            voices_dir = Path.cwd() / "demo" / "voices" / "streaming_model"

        self.voice_presets = {}
        for pt_file in glob.glob(str(voices_dir / "**" / "*.pt"), recursive=True):
            name = Path(pt_file).stem.lower()
            self.voice_presets[name] = str(Path(pt_file).resolve())

        if not self.voice_presets:
            raise RuntimeError(f"No VibeVoice voice presets found in {voices_dir}")

        log(f"[Worker] Found voices: {', '.join(sorted(self.voice_presets.keys()))}")

    def get_voice_path(self, speaker_name: str) -> str:
        speaker_name = speaker_name.lower()
        if speaker_name in self.voice_presets:
            return self.voice_presets[speaker_name]

        matched_path = None
        for preset_name, path in self.voice_presets.items():
            if preset_name in speaker_name or speaker_name in preset_name:
                if matched_path is not None:
                    raise ValueError(f"Multiple voice presets match '{speaker_name}'. Be more specific.")
                matched_path = path

        if matched_path is not None:
            return matched_path

        default_voice = list(self.voice_presets.values())[0]
        log(f"[Worker] No preset for '{speaker_name}', using default {default_voice}")
        return default_voice


def load_vibevoice(args):
    import torch
    from vibevoice.modular.modeling_vibevoice_streaming_inference import (
        VibeVoiceStreamingForConditionalGenerationInference,
    )
    from vibevoice.processor.vibevoice_streaming_processor import VibeVoiceStreamingProcessor

    device = args.device.strip().lower()
    if not device:
        device = "cuda" if torch.cuda.is_available() else "cpu"
    if device == "mpx":
        device = "mps"
    if device == "mps" and not torch.backends.mps.is_available():
        log("[Worker] MPS unavailable, falling back to CPU.")
        device = "cpu"

    if device == "mps":
        load_dtype = torch.float32
        attn_impl = "sdpa"
    elif device == "cuda":
        load_dtype = torch.bfloat16
        attn_impl = "flash_attention_2"
    else:
        load_dtype = torch.float32
        attn_impl = "sdpa"

    log(f"[Worker] Loading processor from {args.model_path}")
    processor = VibeVoiceStreamingProcessor.from_pretrained(args.model_path)

    log(f"[Worker] Loading model device={device}, dtype={load_dtype}, attn={attn_impl}")
    try:
        if device == "mps":
            model = VibeVoiceStreamingForConditionalGenerationInference.from_pretrained(
                args.model_path,
                torch_dtype=load_dtype,
                attn_implementation=attn_impl,
                device_map=None,
            )
            model.to("mps")
        elif device == "cuda":
            model = VibeVoiceStreamingForConditionalGenerationInference.from_pretrained(
                args.model_path,
                torch_dtype=load_dtype,
                device_map="cuda",
                attn_implementation=attn_impl,
            )
        else:
            model = VibeVoiceStreamingForConditionalGenerationInference.from_pretrained(
                args.model_path,
                torch_dtype=load_dtype,
                device_map="cpu",
                attn_implementation=attn_impl,
            )
    except Exception:
        if attn_impl != "flash_attention_2":
            raise
        log("[Worker] flash_attention_2 failed; retrying with sdpa.")
        log(traceback.format_exc())
        model = VibeVoiceStreamingForConditionalGenerationInference.from_pretrained(
            args.model_path,
            torch_dtype=load_dtype,
            device_map=(device if device in ("cuda", "cpu") else None),
            attn_implementation="sdpa",
        )
        if device == "mps":
            model.to("mps")

    model.eval()
    model.set_ddpm_inference_steps(num_steps=5)

    voice_mapper = VoiceMapper()
    voice_sample = voice_mapper.get_voice_path(args.speaker_name)
    log(f"[Worker] Using voice preset {args.speaker_name}: {voice_sample}")
    all_prefilled_outputs = torch.load(
        voice_sample,
        map_location=(device if device != "cpu" else "cpu"),
        weights_only=False,
    )

    return torch, processor, model, all_prefilled_outputs, device


def synthesize(torch, processor, model, all_prefilled_outputs, device, text: str, output_path: Path, cfg_scale: float):
    text = text.strip()
    if not text:
        raise ValueError("Empty text")

    inputs = processor.process_input_with_cached_prompt(
        text=text,
        cached_prompt=all_prefilled_outputs,
        padding=True,
        return_tensors="pt",
        return_attention_mask=True,
    )

    target_device = device if device != "cpu" else "cpu"
    for key, value in inputs.items():
        if torch.is_tensor(value):
            inputs[key] = value.to(target_device)

    start = time.time()
    with torch.inference_mode():
        outputs = model.generate(
            **inputs,
            max_new_tokens=None,
            cfg_scale=cfg_scale,
            tokenizer=processor.tokenizer,
            generation_config={"do_sample": False},
            verbose=False,
            all_prefilled_outputs=copy.deepcopy(all_prefilled_outputs),
        )

    if not outputs.speech_outputs or outputs.speech_outputs[0] is None:
        raise RuntimeError("No audio output generated")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    processor.save_audio(outputs.speech_outputs[0], output_path=str(output_path))
    return time.time() - start


def main():
    args = parse_args()
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    try:
        state = load_vibevoice(args)
        protocol({"type": "ready"})
        log("[Worker] Ready.")
    except Exception as exc:
        protocol({"type": "ready_failed", "error": f"{type(exc).__name__}: {exc}"})
        log(traceback.format_exc())
        return

    torch, processor, model, all_prefilled_outputs, device = state
    request_index = 0

    for line in sys.stdin:
        try:
            request = json.loads(line)
            text = request.get("text", "")
            request_id = request.get("request_id") or str(request_index)
            request_index += 1
            output_path = output_dir / f"{request_id}.wav"
            elapsed = synthesize(
                torch,
                processor,
                model,
                all_prefilled_outputs,
                device,
                text,
                output_path,
                args.cfg_scale,
            )
            protocol({
                "type": "result",
                "ok": True,
                "request_id": request_id,
                "wav_path": str(output_path),
                "bytes": output_path.stat().st_size,
                "seconds": round(elapsed, 3),
            })
        except Exception as exc:
            protocol({
                "type": "result",
                "ok": False,
                "error": f"{type(exc).__name__}: {exc}",
                "traceback": traceback.format_exc(),
            })


if __name__ == "__main__":
    main()
