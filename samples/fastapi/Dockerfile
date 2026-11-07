FROM python:3.13-slim
WORKDIR /app
# Install from the requirements alone, so a code edit keeps the pip layer.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY main.py .
ENV PORT=8000 PYTHONUNBUFFERED=1
EXPOSE 8000
RUN useradd --system --no-create-home sample
USER sample
CMD ["python", "main.py"]
