"""
Esporta DINOv2-small (Meta, licenza Apache 2.0) in ONNX per DupliFoto.

    pip install torch transformers onnx
    python export_dinov2.py            -> crea dinov2-small.onnx (~88 MB)

Uso:  duplifoto "D:\\Foto" --modello dinov2-small.onnx --acceleratore auto

Il modello produce un vettore di 384 numeri per foto: due scatti della stessa scena
hanno vettori molto simili anche se l'inquadratura si è spostata o qualcuno si è mosso.
"""
import torch
from transformers import AutoModel


class Embedder(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, pixel_values):
        # pooler_output = token CLS normalizzato: il riassunto globale dell'immagine
        return self.model(pixel_values=pixel_values).pooler_output


def main():
    model = AutoModel.from_pretrained("facebook/dinov2-small").eval()
    dummy = torch.randn(1, 3, 224, 224)
    torch.onnx.export(
        Embedder(model),
        dummy,
        "dinov2-small.onnx",
        input_names=["pixel_values"],
        output_names=["embedding"],
        # batch dinamico per GPU/CPU; le NPU vengono comunque usate a batch 1 da DupliFoto
        dynamic_axes={"pixel_values": {0: "batch"}, "embedding": {0: "batch"}},
        opset_version=17,
    )
    print("Creato dinov2-small.onnx")


if __name__ == "__main__":
    main()
