"""
Crea un modello ONNX minuscolo (pochi byte) per provare la catena Windows ML / ONNX Runtime
senza scaricare DINOv2. Non riconosce nulla: restituisce il colore medio della foto.

    pip install onnx
    python crea_modello_prova.py       -> crea modello-prova.onnx

Uso:  duplifoto "D:\\Foto" --modello modello-prova.onnx --acceleratore cpu
Stesso ingresso e stessa uscita di export_dinov2.py: pixel_values [batch, 3, 224, 224] -> embedding [batch, d].
"""
import onnx
from onnx import TensorProto, helper


def main():
    pixels = helper.make_tensor_value_info("pixel_values", TensorProto.FLOAT, ["batch", 3, 224, 224])
    embedding = helper.make_tensor_value_info("embedding", TensorProto.FLOAT, ["batch", 3])
    nodes = [
        helper.make_node("GlobalAveragePool", ["pixel_values"], ["pooled"]),  # [batch, 3, 1, 1]
        helper.make_node("Flatten", ["pooled"], ["embedding"], axis=1),        # [batch, 3]
    ]
    graph = helper.make_graph(nodes, "colore-medio", [pixels], [embedding])
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 17)])
    model.ir_version = 8  # compatibile con tutte le versioni recenti di ONNX Runtime
    onnx.checker.check_model(model)
    onnx.save(model, "modello-prova.onnx")
    print("Creato modello-prova.onnx")


if __name__ == "__main__":
    main()
